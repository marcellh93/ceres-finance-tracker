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
[Collection("IntegrationTests")]
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
    /// The backstop: a suite that forgets to purge still gets cleaned up, because the
    /// sweep keys on the owner being gone rather than on anyone remembering to call it.
    /// This is the case that actually matters — 47 of 69 user-creating test files never
    /// call PurgeUserAsync.
    /// </summary>
    [Fact]
    public async Task SweepOrphanedRowsAsync_removes_rows_a_forgetful_suite_left_behind()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"sweep{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Delete the user WITHOUT purging — exactly what the 47 files do.
        await um.DeleteAsync(user);

        var stranded = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id);
        stranded.Should().BeGreaterThan(0, "the leak must exist for the sweep to prove anything");

        await UserOwnedCleanup.SweepOrphanedRowsAsync(db);

        var remaining = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id);
        remaining.Should().Be(0, "the sweep must reclaim rows whose owner no longer exists");
    }

    /// <summary>
    /// The guard that matters more than the sweep itself. Sentinel-owned rows are
    /// fixtures with fixed ids and NO AspNetUsers row by design, so a naive
    /// "delete where the owner is missing" would take them — which happened on
    /// 2026-08-21, broke 243 tests, and needed a pg_dump restore.
    /// </summary>
    [Fact]
    public async Task SweepOrphanedRowsAsync_spares_the_sentinel_fixtures()
    {
        var sentinel = new Guid("00000000-0000-0000-0000-000000000001");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var before = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == sentinel);
        before.Should().BeGreaterThan(0, "the sentinel fixtures must exist for this to prove anything");

        (await db.Users.AnyAsync(u => u.Id == sentinel)).Should().BeFalse(
            "the sentinel deliberately has no AspNetUsers row — which is exactly why a naive "
            + "orphan sweep would delete its fixtures");

        await UserOwnedCleanup.SweepOrphanedRowsAsync(db);

        var after = await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == sentinel);
        after.Should().Be(before, "sentinel fixtures must survive the sweep untouched");
    }

    /// <summary>
    /// Documents WHY the helper is needed: deleting the user on its own strands the
    /// categories. If this ever fails, real FK cascades exist and the helper can go.
    /// </summary>
    [Fact]
    public async Task Deleting_a_user_alone_strands_categories_which_is_why_purge_exists()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"strand{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await um.DeleteAsync(user);

        var stranded = await db.Categories.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == user.Id);
        stranded.Should().BeGreaterThan(0,
            "no FK cascades from AspNetUsers to Categories — this is the leak the helper covers");

        // Leave the database as clean as we found it.
        await UserOwnedCleanup.PurgeUserAsync(db, user.Id);
        (await db.Categories.IgnoreQueryFilters().CountAsync(c => c.UserId == user.Id))
            .Should().Be(0);
    }

    /// <summary>
    /// The table list is derived from the EF model, so a newly added IUserOwned
    /// entity is covered without editing a registry.
    /// </summary>
    [Fact]
    public async Task Cleanup_covers_every_user_owned_table_in_the_model()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var modelTables = UserOwnedModel.RlsTables(db.Model)
            .Select(t => t.PostgresTableName).ToHashSet(StringComparer.Ordinal);

        modelTables.Should().Contain("Categories", "the table that leaked");
        modelTables.Should().Contain("Accounts");

        // A purge of a nonexistent user must be a harmless no-op across every table.
        var act = async () => await UserOwnedCleanup.PurgeUserAsync(db, Guid.NewGuid());
        await act.Should().NotThrowAsync(
            "the helper must tolerate a user with no rows, on every table in the model");
    }
}
