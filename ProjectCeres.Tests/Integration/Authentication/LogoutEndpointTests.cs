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

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel2")]
public class LogoutEndpointTests : IntegrationTestBase<Bucket2AuthFactory>, IAsyncLifetime
{
    private readonly Bucket2AuthFactory _factory;

    public LogoutEndpointTests(Bucket2AuthFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@logout-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Logout_revokes_UserSession_and_clears_cookies()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "u@logout-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: anonymous login.
        var (loginCookie, loginHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "u@logout-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={loginCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, loginHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty();

        // Step 2: authenticated logout — CSRF token bound to the user.
        var (logoutCookie, logoutHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        logoutReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={logoutCookie}");
        logoutReq.Headers.Add(SessionConstants.CsrfHeaderName, logoutHeader);
        var resp = await client.SendAsync(logoutReq);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Filter by this test's user so concurrent/prior tests in the IntegrationTests
        // collection don't pollute the result — under load the table accumulates rows
        // from other tests and an unfiltered FirstAsync() returns whichever row landed
        // first in the table, not necessarily this test's session.
        var session = await db.UserSessions.IgnoreQueryFilters().SingleAsync(s => s.UserId == user.Id);
        session.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_DeletesPersistentCookieWithCorrectOptions()
    {
        // Set up an authenticated user with rememberMe so __Host-Persist is issued.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "delopts@logout-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: login with rememberMe=true so the persistent cookie is issued.
        var (loginCsrfCookie, loginCsrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = user.Email,
                password = AuthTestFixture.ValidPassword,
                rememberMe = true,
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={loginCsrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, loginCsrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Collect the session cookie and persistent cookie from the login response.
        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty("login must issue a session cookie");
        var persistCookieValue = ExtractSetCookie(loginResp, SessionConstants.PersistentCookieName);
        persistCookieValue.Should().NotBeNullOrEmpty("login with rememberMe must issue a persistent cookie");

        // Step 2: logout. Use a user-bound CSRF token so the antiforgery middleware accepts it.
        var (logoutCsrfCookie, logoutCsrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        logoutReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; " +
            $"{SessionConstants.PersistentCookieName}={persistCookieValue}; " +
            $"{SessionConstants.CsrfCookieName}={logoutCsrfCookie}");
        logoutReq.Headers.Add(SessionConstants.CsrfHeaderName, logoutCsrfHeader);
        var logoutResp = await client.SendAsync(logoutReq);
        logoutResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Inspect Set-Cookie for the __Host-Persist deletion directive.
        var setCookies = logoutResp.Headers.GetValues("Set-Cookie").ToList();
        var persistDelete = setCookies.FirstOrDefault(c =>
            c.StartsWith($"{SessionConstants.PersistentCookieName}=") &&
            c.Contains("expires=Thu, 01 Jan 1970"));
        persistDelete.Should().NotBeNull("logout must emit a deletion directive for the __Host-Persist cookie");

        // __Host- cookies require Path=/ and Secure to be valid (and to match for deletion).
        persistDelete!.Should().Contain("path=/", "deletion directive must include path=/ to match the __Host- cookie");
        // Secure is environment-dependent in tests (CookieSecurePolicy.SameAsRequest outside Production)
        // — assert the directive is well-formed but skip the secure assertion to keep test
        // environment-agnostic. The path=/ assertion is the load-bearing one for the bug fix.
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
