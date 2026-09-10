using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Pins the orphan-row leak fixed on 2026-08-21. Deleting a user does NOT cascade
/// to their user-owned rows — most of those tables carry a bare UserId column with
/// no foreign key to AspNetUsers — so registration's 26 seeded categories survived
/// every test run. 57,267 orphaned rows had accumulated in the shared test database.
/// </summary>
[Collection("IntegrationParallel1")]
public class UserOwnedCleanupTests
{
    private const string EmailSuffix = "@cleanup-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public UserOwnedCleanupTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    /// <summary>
    /// The regression itself: registering seeds categories, and deleting the user
    /// alone leaves them behind. PurgeUserAsync must remove them.
    /// </summary>
    [Fact]
    public async Task PurgeUserAsync_removes_seeded_categories_that_user_deletion_leaves_behind()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"purge{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var seeded = await db.Categories.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == user.Id);
        seeded.Should().BeGreaterThan(0,
            "registration seeds default categories — without them this test proves nothing");

        await UserOwnedCleanup.PurgeUserAsync(db, user.Id);
        await um.DeleteAsync(user);

        var remaining = await db.Categories.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == user.Id);
        remaining.Should().Be(0,
            "purging before deletion must leave no orphaned categories");
    }

    /// <summary>
    /// The leak as it actually is: a suite that registers a user and simply walks away.
    /// No deletion, no purge — so nothing is ever "orphaned" and the user keeps holding
    /// its 26 seeded categories forever. This is the shape 579 leftover users had on
    /// 2026-08-24; the first version of the sweep missed all of them because it only
    /// looked for rows whose user was already gone.
    /// </summary>
    [Fact]
    public async Task SweepAbandonedTestUsersAsync_removes_users_a_suite_walked_away_from()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"sweep{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // No DeleteAsync, no PurgeUserAsync — the user is simply abandoned.
        var held = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id);
        held.Should().BeGreaterThan(0, "the leak must exist for the sweep to prove anything");

        await UserOwnedCleanup.SweepAbandonedTestUsersAsync(db);

        var rowsAfter = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id);
        rowsAfter.Should().Be(0, "the sweep reclaims rows held by abandoned test users");

        var userAfter = await db.Users.IgnoreQueryFilters().CountAsync(u => u.Id == user.Id);
        userAfter.Should().Be(0, "and the user row itself, or the rows come back next run");
    }

    /// <summary>
    /// The guard that matters. The sentinel owns 26 seeded categories and three fixture
    /// Accounts that NO migration recreates. A broken sweep destroyed them on 2026-08-24
    /// and cost 249 failing tests plus a hand-written restore, so this asserts survival
    /// directly rather than trusting the WHERE clause to be right.
    /// </summary>
    [Fact]
    public async Task SweepAbandonedTestUsersAsync_spares_the_sentinel_fixtures()
    {
        var sentinel = new Guid("00000000-0000-0000-0000-000000000001");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var categoriesBefore = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == sentinel);
        var accountsBefore   = await db.Accounts.IgnoreQueryFilters().CountAsync(a => a.UserId == sentinel);
        categoriesBefore.Should().BeGreaterThan(0, "the fixtures must exist for this to prove anything");
        accountsBefore.Should().BeGreaterThan(0);

        await UserOwnedCleanup.SweepAbandonedTestUsersAsync(db);

        var categoriesAfter = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == sentinel);
        var accountsAfter   = await db.Accounts.IgnoreQueryFilters().CountAsync(a => a.UserId == sentinel);
        categoriesAfter.Should().Be(categoriesBefore, "sentinel categories are seeded fixtures, not leaked rows");
        accountsAfter.Should().Be(accountsBefore, "and the fixture Accounts come back from no migration at all");
    }
}
