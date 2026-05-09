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
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
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
}
