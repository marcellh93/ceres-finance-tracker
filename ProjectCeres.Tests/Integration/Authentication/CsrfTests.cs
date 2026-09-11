using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel2")]
public class CsrfTests : IntegrationTestBase<Bucket2AuthFactory>, IAsyncLifetime
{
    private readonly Bucket2AuthFactory _factory;

    public CsrfTests(Bucket2AuthFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@csrf-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task State_changing_request_without_csrf_token_returns_400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // No Cookie header, no X-XSRF-TOKEN header.
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "x@csrf-test.local",
            password = "any-long-password-here-15+",
            rememberMe = false
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "antiforgery filter must reject state-changing requests without X-XSRF-TOKEN");
    }

    [Fact]
    public async Task CsrfCookiePresent_HeaderMissing_Returns400()
    {
        // Provide the __Host-XSRF cookie but omit the X-XSRF-TOKEN header entirely.
        // Antiforgery validation requires BOTH the cookie AND the matching header.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrfCookie, _) = AuthTestFixture.MintCsrf(_factory);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "cookie-only@csrf-test.local",
                password = "any-long-password-here-15+",
                rememberMe = false,
            }),
        };
        // Cookie present — header deliberately absent
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "antiforgery must reject when the cookie is present but the X-XSRF-TOKEN header is missing");
    }

    [Fact]
    public async Task CsrfTokenFromUserA_OnUserBSession_Rejected()
    {
        // Register A and B, log in as A, capture A's session cookie and a CSRF token
        // bound to A's principal. Then send the request with A's CSRF + B's session.
        // The framework's antiforgery binds the request token to the authenticated
        // principal (NameIdentifier), so cross-session use must be rejected.
        //
        // If this test FAILS (i.e. A's token works on B's session), that is a real
        // CSRF gap — report rather than fix.

        var userA = await AuthTestFixture.RegisterUserAsync(_factory, "csrf-a@csrf-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, "csrf-b@csrf-test.local");

        var clientA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Log A in through the full password flow (no MFA enrolled) to get a real session cookie
        var loginA = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginA.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "userA must log in successfully");
        var sessionCookieA = ExtractSetCookie(loginA, SessionConstants.SessionCookieName);
        sessionCookieA.Should().NotBeNullOrEmpty("userA login must issue a session cookie");

        // Log B in similarly to get B's session cookie
        var clientB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginB = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientB, "/api/auth/login",
            new { email = userB.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginB.StatusCode.Should().Be(HttpStatusCode.NoContent, "userB must log in successfully");
        var sessionCookieB = ExtractSetCookie(loginB, SessionConstants.SessionCookieName);
        sessionCookieB.Should().NotBeNullOrEmpty("userB login must issue a session cookie");

        // Mint a CSRF token bound to A's identity
        var (csrfCookieA, csrfHeaderA) = AuthTestFixture.MintCsrf(_factory, userA.Id);

        // Make a state-changing request using A's CSRF tokens but B's session cookie
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookieB}; {SessionConstants.CsrfCookieName}={csrfCookieA}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeaderA);

        var resp = await clientA.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "antiforgery must reject A's CSRF token when used with B's session — token is bound to A's principal");
    }

    [Fact]
    public async Task CsrfEndpoint_ResponseShape_HasTokenInCookie()
    {
        // GET /api/auth/csrf must return 204 and set the __Host-XSRF cookie.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var resp = await client.GetAsync("/api/auth/csrf");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "GET /api/auth/csrf must return 204");

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToList()
            : new List<string>();

        setCookies.Should().Contain(c => c.StartsWith($"{SessionConstants.CsrfCookieName}="),
            $"response must set the {SessionConstants.CsrfCookieName} cookie");
    }

    [Fact]
    public async Task LoginTotp_WithoutCsrfHeader_Returns400()
    {
        // POST /api/auth/login/totp without any CSRF header must return 400.
        // Antiforgery applies to all POST endpoints including /login/totp.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // No CSRF cookie, no CSRF header — antiforgery should reject.
        var resp = await client.PostAsJsonAsync("/api/auth/login/totp", new { code = "000000" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "antiforgery must reject POST /api/auth/login/totp when no CSRF header is present");
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
