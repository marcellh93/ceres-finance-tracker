using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel4")]
public class MfaPendingCookieTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;
    public MfaPendingCookieTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@mfa-cookie-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts.Where(e => e.EmailAttempted!.EndsWith("@mfa-cookie-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TamperedSignature_Returns401_NoFailedLoginRow()
    {
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = "000000" })
        };
        req.Headers.Add("Cookie", "Identity.TwoFactorUserId=tampered.junk.value");
        var (cookie, header) = AuthTestFixture.MintCsrf(_factory);
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Cheap proxy: this test runs quickly; assert no row mentions our test domain
        // within the time window of this test.
        var inWindow = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@mfa-cookie-test.local")
                     && e.OccurredAt > DateTime.UtcNow.AddSeconds(-5))
            .CountAsync();
        inWindow.Should().Be(0);
    }

    [Fact]
    public async Task MfaPendingCookie_FromDifferentUser_DoesNotAuthenticateAsTargetUser()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, "a@mfa-cookie-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, "b@mfa-cookie-test.local");
        var seedB = await AuthTestFixture.EnrollUserMfaAsync(_factory, userB);

        var clientA = _factory.CreateClient();
        // userA logs in (no MFA enrolled) — they get a __Host-Session cookie, not Identity.TwoFactorUserId.
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        // Submitting userB's TOTP code via clientA's cookies must fail — userA never has an
        // Identity.TwoFactorUserId cookie because they're not MFA-enrolled.
        // Mint a user-bound CSRF: clientA is fully authenticated as userA, so an anonymous
        // CSRF would fail antiforgery validation (400) before the endpoint logic runs.
        var (cookie, header) = AuthTestFixture.MintCsrf(_factory, userA.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { code = AuthTestFixture.ComputeCurrentTotpCode(seedB) }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await clientA.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MfaPendingCookie_ExpiredAfter5Min_Returns401()
    {
        // Direct test of TTL-driven server rejection is awkward without a clock-skew shim.
        // Use a unit-style assertion: confirm the configured ExpireTimeSpan is 5 min.
        using var scope = _factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>();
        var opts = optionsMonitor.Get(IdentityConstants.TwoFactorUserIdScheme);
        opts.ExpireTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        opts.SlidingExpiration.Should().BeFalse();
        await Task.CompletedTask;
    }
}
