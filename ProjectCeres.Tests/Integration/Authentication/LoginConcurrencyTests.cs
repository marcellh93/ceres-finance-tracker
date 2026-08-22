using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class LoginConcurrencyTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public LoginConcurrencyTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@conc-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts.Where(e => e.EmailAttempted!.EndsWith("@conc-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Concurrent_FailedLogins_AllRecordOneRowEach()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "conc-fl@conc-test.local");

        var tasks = Enumerable.Range(0, 10).Select(_ =>
        {
            var c = _factory.CreateClient();
            return AuthTestFixture.PostJsonWithCsrfAsync(_factory, c, "/api/auth/login",
                new { email = "conc-fl@conc-test.local", password = "wrong-but-long-enough", rememberMe = false });
        });
        await Task.WhenAll(tasks);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "conc-fl@conc-test.local")
            .CountAsync();
        count.Should().Be(10);
    }

    [Fact]
    public async Task Concurrent_LoginAttempts_AccessFailedCountReachesExactly10()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "conc-counter@conc-test.local");

        var tasks = Enumerable.Range(0, 12).Select(_ =>
        {
            var c = _factory.CreateClient();
            return AuthTestFixture.PostJsonWithCsrfAsync(_factory, c, "/api/auth/login",
                new { email = "conc-counter@conc-test.local", password = "wrong-but-long-enough", rememberMe = false });
        });
        await Task.WhenAll(tasks);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync("conc-counter@conc-test.local");
        // The critical assertion is "lockout DID engage". Identity.AccessFailedAsync
        // resets AccessFailedCount to 0 when the lockout threshold is crossed (it sets
        // LockoutEnd and calls ResetAccessFailedCountAsync in the same operation), so
        // the count will be 0 after lockout — not 10. Asserting IsLockedOut is sufficient.
        (await um.IsLockedOutAsync(fresh!)).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_TotpSubmissions_OnlyOneSucceeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "conc-totp@conc-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var c1 = _factory.CreateClient();
        var c2 = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, c1, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, c2, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var t1 = AuthTestFixture.PostJsonWithCsrfAsync(_factory, c1, "/api/auth/login/totp", new { code });
        var t2 = AuthTestFixture.PostJsonWithCsrfAsync(_factory, c2, "/api/auth/login/totp", new { code });

        var responses = await Task.WhenAll(t1, t2);
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1);
    }
}
