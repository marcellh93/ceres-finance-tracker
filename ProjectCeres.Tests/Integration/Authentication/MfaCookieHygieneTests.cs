using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class MfaCookieHygieneTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaCookieHygieneTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@cookies-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task LockoutResponse_ClearsTwoFactorUserIdAndRememberMeCookies()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "lockcookies@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "000000" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.ToList() : new List<string>();
        setCookies.Should().Contain(c => c.Contains("Identity.TwoFactorUserId") && c.Contains("expires=Thu, 01 Jan 1970"));
        setCookies.Should().Contain(c => c.Contains("Mfa.RememberMe") && c.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task TwoFactorUserIdCookie_ExpiresIn5Minutes()
    {
        // The 5-min TTL is enforced server-side via ExpireTimeSpan, not via the
        // browser cookie's Expires header. Browser-visible expiry only fires when
        // IsPersistent=true, which we deliberately do NOT set on the MFA-pending
        // cookie (a persistent MFA-pending cookie would survive browser close,
        // defeating the strict TTL). The server enforces rejection after 5 min
        // regardless of what the browser-side cookie says.
        using var scope = _factory.Services.CreateScope();
        var optionsMonitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
                Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>();
        var opts = optionsMonitor.Get(IdentityConstants.TwoFactorUserIdScheme);
        opts.ExpireTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
        opts.SlidingExpiration.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LockedAccount_BackupCodeRecovery_IssuesCleanSessionCookie()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "fresh@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        resp.Headers.TryGetValues("Set-Cookie", out var setCookies);
        setCookies!.Should().Contain(c => c.StartsWith("__Host-Session="));
    }
}
