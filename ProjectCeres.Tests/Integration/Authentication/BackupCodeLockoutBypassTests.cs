using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

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
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
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

    [Fact]
    public async Task LockedAccount_ValidBackupCode_CompletesLogin()
        => await Setup_LockedUser_AndLogin_With(useTotp: false);

    [Fact]
    public async Task LockedAccount_ValidTotp_CompletesLogin_AndClearsLockout()
        => await Setup_LockedUser_AndLogin_With(useTotp: true);

    [Fact]
    public async Task BackupCodeOnNonLockedAccount_StillWorks()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "no-lock@bclock-test.local");
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
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BackupCodeWithoutMfaPendingCookie_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "AAAA-AAAA-AAAA-AAAA" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BackupCodeConsumed_CannotBeReusedEvenAfterLockoutBypass()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "consume@bclock-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        IReadOnlyList<string> codes;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            codes = await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client1 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var first = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login/totp",
            new { code = codes[0] });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var client2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var second = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login/totp",
            new { code = codes[0] });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BackupCodeRecoveryDuringLockout_DecrementsRemainingCount()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "decrement@bclock-test.local");
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
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = codes[0] });

        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var consumed = await db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id && c.UsedAt != null)
            .CountAsync();
        consumed.Should().Be(1);
    }

    private async Task Setup_LockedUser_AndLogin_With(bool useTotp)
    {
        var email = useTotp ? "totp-bypass@bclock-test.local" : "bc-bypass@bclock-test.local";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
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

        var code = useTotp ? AuthTestFixture.ComputeCurrentTotpCode(seed) : codes[0];
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        if (useTotp)
        {
            using var scope2 = _factory.Services.CreateScope();
            var um = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var after = await um.FindByIdAsync(user.Id.ToString());
            after!.AccessFailedCount.Should().Be(0);
            after.LockoutEnd.Should().BeNull();
        }
    }
}
