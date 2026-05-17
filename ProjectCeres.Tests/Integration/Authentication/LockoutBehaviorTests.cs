using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class LockoutBehaviorTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LockoutBehaviorTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@lockout-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@lockout-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task WrongTotp_DoesNotIncrementPasswordLockoutCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "wrong-totp@lockout-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient();

        // Step 1: pass password to obtain MFA-pending cookie (cookie auto-stored by HttpClient).
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.EnsureSuccessStatusCode();

        // Step 2: submit 5 wrong TOTP codes. Each is 6 digits but not the real one.
        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "000000" });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        // Pre-6b.2 bug: AccessFailedCount would be 5. After 6b.2 fix: still 0.
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(0,
            "wrong TOTP codes must not increment the password lockout counter");
    }

    [Fact]
    public async Task WrongBackupCode_DoesNotIncrementPasswordLockoutCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "wrong-bc@lockout-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "AAAA-AAAA-AAAA-AAAA" });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task WrongPasswordTenTimes_LocksAccount()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "ten@lockout-test.local");
        var client = _factory.CreateClient();

        for (int i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        var body = await eleventh.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("ACCOUNT_LOCKED_OUT");
    }

    [Fact]
    public async Task SuccessfulPasswordLogin_ResetsCounter()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "reset@lockout-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            for (int i = 0; i < 5; i++) await um.AccessFailedAsync(fresh!);
        }

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.EnsureSuccessStatusCode();

        using var scope2 = _factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        after!.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task RateLimitRejection_DoesNotCountTowardLockout()
    {
        // The default test factory has the no-op limiter so we exercise this
        // logically. The recorder/controller wiring must NOT call AccessFailedAsync
        // on the rate-limit-rejection path. Architecture test #51 enforces this
        // structurally; here we sanity-check at the integration level by firing
        // many requests (no-op limiter accepts them all) and confirming each one
        // contributes exactly 1 to AccessFailedCount.
        //
        // Stage 9.1.5.b note: this test uses the no-op rate-limit factory, so
        // OnRejected never runs and the LockoutCache lookup path is not exercised
        // here. The cache-aware envelope behavior is covered by
        // RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited
        // and siblings in RateLimitedAuthEndpointTests.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "rl-no-lock@lockout-test.local");
        var client = _factory.CreateClient();

        for (int i = 0; i < 3; i++)
        {
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        }

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        fresh!.AccessFailedCount.Should().Be(3); // 3 wrong-password requests, 3 increments
    }

    [Fact]
    public async Task LockoutSurvivesProcessRestart()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "restart@lockout-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        // Simulate restart by creating a fresh DI scope (the DB row is already persisted).
        using var scope2 = _factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = await um2.FindByIdAsync(user.Id.ToString());
        (await um2.IsLockedOutAsync(after!)).Should().BeTrue();
    }

    [Fact]
    public async Task LockoutAutoLifts_AfterDefaultLockoutTimeSpan_Elapses()
    {
        // Set a future lockout via UserManager (same as production lock path).
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "autolift@lockout-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        // Backdate the LockoutEnd to 1ms in the past so the lockout window appears elapsed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    u => u.LockoutEnd,
                    DateTimeOffset.UtcNow.AddMilliseconds(-1)));
        }

        // Login with the correct password — lockout window is now in the past.
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        // 204 = success (no MFA enrolled). The lockout window has elapsed so login should succeed.
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent,
            "login must succeed once the lockout window has elapsed (Identity auto-lifts expired lockouts)");
    }
}
