using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class BackupCodeLockoutBypassTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public BackupCodeLockoutBypassTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@bclock-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task LockedAccount_BackupCodeSuccess_ClearsLockoutEndAndCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bypass@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Generate fresh backup codes for this user via the service.
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient();

        // Step 1: hit the password step (account is currently NOT locked) so an
        // Identity.TwoFactorUserId cookie is issued. THEN re-lock, then attempt
        // the backup code. This simulates "user got locked between password + TOTP."
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.EnsureSuccessStatusCode(); // RequiresTwoFactor → 200 with { requiresTotp: true }

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var totpResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "valid backup code must complete login even when account is locked");

        using var verifyScope = _factory.Services.CreateScope();
        var um2 = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        after!.AccessFailedCount.Should().Be(0);
        after.LockoutEnd.Should().BeNull();
    }
}
