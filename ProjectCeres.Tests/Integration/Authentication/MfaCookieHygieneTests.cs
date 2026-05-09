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
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "ttl@cookies-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.Headers.TryGetValues("Set-Cookie", out var cookies);
        var twoFactor = cookies!.First(c => c.StartsWith("Identity.TwoFactorUserId="));
        // Either max-age=300 or an Expires within 4-6 minutes from now.
        twoFactor.Should().MatchRegex(@"max-age=300|expires=.+");
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
