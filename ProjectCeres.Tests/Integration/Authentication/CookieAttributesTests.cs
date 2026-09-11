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

[Collection("IntegrationParallel3")]
public class CookieAttributesTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;

    public CookieAttributesTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@cookie-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_response_sets_session_and_persist_cookies_with_expected_attributes()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "ck@cookie-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "ck@cookie-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = true
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var resp = await client.SendAsync(req);

        var setCookies = resp.Headers.GetValues("Set-Cookie").ToList();

        AssertHostCookieContract(setCookies, SessionConstants.SessionCookieName,    expectedHttpOnly: true);
        AssertHostCookieContract(setCookies, SessionConstants.PersistentCookieName, expectedHttpOnly: true);
    }

    [Fact]
    public async Task Csrf_endpoint_sets_xsrf_cookie_with_expected_attributes()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var resp = await client.GetAsync("/api/auth/csrf");

        var setCookies = resp.Headers.GetValues("Set-Cookie").ToList();
        AssertHostCookieContract(setCookies, SessionConstants.CsrfCookieName, expectedHttpOnly: false);
    }

    // ── Edge-case batch A (Stage 6b.3) ──────────────────────────────────────

    [Fact]
    public async Task TotpSuccess_SetsSessionCookieWithExpectedAttributes()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "totp-ck@cookie-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: credential login — returns 200 + Identity.TwoFactorUserId cookie.
        var (csrfCookie1, csrfHeader1) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                email = "totp-ck@cookie-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie1}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader1);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Extract the Identity.TwoFactorUserId cookie value.
        var twoFactorCookieValue = loginResp.Headers
            .GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .FirstOrDefault(c => c.StartsWith("Identity.TwoFactorUserId="))
            ?.Split('=', 2)[1];
        twoFactorCookieValue.Should().NotBeNullOrEmpty();

        // Step 2: TOTP submit — should issue the session cookie.
        var (csrfCookie2, csrfHeader2) = AuthTestFixture.MintCsrf(_factory);
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { code }),
        };
        totpReq.Headers.Add("Cookie",
            $"Identity.TwoFactorUserId={twoFactorCookieValue}; {SessionConstants.CsrfCookieName}={csrfCookie2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader2);
        var totpResp = await client.SendAsync(totpReq);

        totpResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        var setCookies = totpResp.Headers.GetValues("Set-Cookie").ToList();
        AssertHostCookieContract(setCookies, SessionConstants.SessionCookieName, expectedHttpOnly: true);
    }

    private static void AssertHostCookieContract(IEnumerable<string> setCookies, string name, bool expectedHttpOnly)
    {
        var match = setCookies.FirstOrDefault(c => c.StartsWith(name + "="));
        match.Should().NotBeNull("expected {0} to be set on login response", name);

        // Lower-case for case-insensitive comparison.
        var lower = match!.ToLowerInvariant();

        // Path=/ and SameSite=Lax are checked unconditionally.
        lower.Should().Contain("path=/", because: $"{name} must have Path=/");
        lower.Should().Contain("samesite=lax", because: $"{name} must be SameSite=Lax");
        lower.Should().NotContain("domain=", because: $"{name} must not have a Domain attribute");

        // HttpOnly: required for session/persistent, forbidden for XSRF (SPA must read it).
        if (expectedHttpOnly)
            lower.Should().Contain("httponly", because: $"{name} must be HttpOnly");
        else
            lower.Should().NotContain("httponly", because: $"{name} must be JS-readable for the SPA");

        // Secure: enforced via SecurePolicy.SameAsRequest in non-prod environments. The
        // test runs over HTTP, so Set-Cookie omits Secure. Production sets Always (and
        // the __Host- prefix browser-side requires Secure regardless). The cookie name
        // prefix itself is the test's primary contract: starts with "__Host-".
        match.Should().StartWith("__Host-", because: $"{name} must use the __Host- prefix");
    }
}
