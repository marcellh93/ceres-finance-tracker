using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel3")]
public class TotpReplayDuringLockoutTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public TotpReplayDuringLockoutTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@replay-lock-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task ReplayedTotpDuringLockout_StillRejected()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "replay@replay-lock-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client1 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var first = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client1, "/api/auth/login/totp",
            new { code });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-lock — first login cleared LockoutEnd. Re-establish so we can prove replay rejection
        // applies even on the lockout-bypass path.
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        var client2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var second = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client2, "/api/auth/login/totp",
            new { code });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
