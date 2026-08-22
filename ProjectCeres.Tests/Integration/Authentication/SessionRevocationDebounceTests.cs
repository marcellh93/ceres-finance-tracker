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
public class SessionRevocationDebounceTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public SessionRevocationDebounceTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@debounce-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    /// <summary>
    /// Helper: fully authenticate the user (no MFA) and return a client carrying the session cookie,
    /// plus the session row's id.
    /// </summary>
    private async Task<(HttpClient client, Guid sessionId, ApplicationUser user)> SetupAsync(string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.EnsureSuccessStatusCode();

        // Find the session row that the login just created.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();
        return (client, session.Id, user);
    }

    [Fact]
    public async Task LastUsedAt_DoesNotUpdate_WithinDebounceWindow()
    {
        var (client, sessionId, _) = await SetupAsync("within@debounce-test.local");

        // Fire one authenticated request, capture LastUsedAt.
        var first = await client.GetAsync("/api/categories");
        first.EnsureSuccessStatusCode();

        DateTime firstLastUsed;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.UserSessions.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == sessionId);
            firstLastUsed = s.LastUsedAt;
        }

        // Immediately fire several more requests (well within the 60s debounce window).
        for (int i = 0; i < 5; i++)
        {
            var resp = await client.GetAsync("/api/categories");
            resp.EnsureSuccessStatusCode();
        }

        // LastUsedAt must NOT have moved.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.UserSessions.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == sessionId);
            s.LastUsedAt.Should().Be(firstLastUsed,
                "within the 60s debounce window, LastUsedAt must not be re-written on every request");
        }
    }

    [Fact]
    public async Task LastUsedAt_Updates_AfterDebounceWindow()
    {
        var (client, sessionId, _) = await SetupAsync("after@debounce-test.local");

        // Fire one request to bootstrap; then artificially backdate LastUsedAt to simulate
        // the debounce window having elapsed (faster than waiting 60s in the test).
        var bootstrap = await client.GetAsync("/api/categories");
        bootstrap.EnsureSuccessStatusCode();

        var backdated = DateTime.UtcNow - TimeSpan.FromSeconds(90);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserSessions
                .IgnoreQueryFilters()
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastUsedAt, backdated));
        }

        // Fire one more request — the debounce window has elapsed, so LastUsedAt should now update.
        var second = await client.GetAsync("/api/categories");
        second.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.UserSessions.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == sessionId);
            s.LastUsedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5),
                "after the debounce window elapses, LastUsedAt must update on the next request");
            s.LastUsedAt.Should().NotBe(backdated);
        }
    }
}
