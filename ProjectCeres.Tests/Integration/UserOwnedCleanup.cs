using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Deletes every user-owned row belonging to a test user.
///
/// Test classes delete their users via UserManager.DeleteAsync in DisposeAsync, but
/// most user-owned tables (Categories, Accounts, Transactions, …) carry a bare
/// <c>UserId</c> column with no foreign key to AspNetUsers, so nothing cascades.
/// Registration seeds 26 default categories per user, which meant each run left ~26
/// permanently orphaned rows behind: by 2026-08-21 the shared test database held
/// 57,267 orphaned rows, 53,248 of them categories, accumulated since 2026-06-15.
///
/// Call <see cref="PurgeUserAsync"/> before deleting the user.
/// </summary>
public static class UserOwnedCleanup
{
    /// <summary>
    /// Deletes all rows owned by <paramref name="userId"/> across every user-owned
    /// table. Table list comes from the EF model, so a new IUserOwned entity is
    /// covered automatically — no registry to update.
    /// </summary>
    public static async Task PurgeUserAsync(AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        foreach (var table in DeletionOrder(db))
        {
            // Raw SQL: the caller may not have a DbSet for every table, and query
            // filters would otherwise scope the delete to the current user.
            await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE \"UserId\" = {{0}}", [userId], ct);
        }
    }

    /// <summary>
    /// Deletes all rows owned by any of <paramref name="userIds"/>. Cheaper than
    /// calling <see cref="PurgeUserAsync"/> in a loop for multi-user fixtures.
    /// </summary>
    public static async Task PurgeUsersAsync(
        AppDbContext db, IEnumerable<Guid> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        foreach (var table in DeletionOrder(db))
        {
            await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE \"UserId\" = ANY({{0}})", [ids], ct);
        }
    }

    /// <summary>
    /// Deletes every user-owned row whose owning user no longer exists.
    ///
    /// The per-user purge above only helps suites that remember to call it, and most do
    /// not: of 69 test files that create users, 22 call it. The rest delete the user (or
    /// nothing at all) and leave 26 seeded categories behind each time, because no
    /// user-owned table has a foreign key to AspNetUsers. By 2026-08-24 that had reached
    /// 229,840 categories and pushed the suite from ~3:30 to over 11 minutes.
    ///
    /// This is the backstop: a sweep that does not depend on any individual suite
    /// remembering anything. Safe to run at any time — a row whose UserId is absent from
    /// AspNetUsers can never be read by the application, which filters every query by the
    /// current user.
    ///
    /// The sentinel user is EXCLUDED. Its rows are fixtures with fixed ids and no
    /// AspNetUsers row by design; deleting them broke 243 tests on 2026-08-21 and needed
    /// a pg_dump restore.
    /// </summary>
    public static async Task<int> SweepOrphanedRowsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var sentinel = new Guid("00000000-0000-0000-0000-000000000001");
        var total = 0;

        foreach (var table in DeletionOrder(db))
        {
            total += await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" x WHERE x.\"UserId\" IS NOT NULL "
                + "AND x.\"UserId\" <> {0} "
                + "AND NOT EXISTS (SELECT 1 FROM \"AspNetUsers\" u WHERE u.\"Id\" = x.\"UserId\")",
                [sentinel], ct);
        }

        return total;
    }

    /// <summary>
    /// User-owned tables ordered so children are deleted before the rows they
    /// reference. Accounts and Categories go last: Transactions, Budgets and the
    /// import staging tables all point at them.
    /// </summary>
    private static IEnumerable<string> DeletionOrder(AppDbContext db)
    {
        var last = new[] { "Accounts", "Categories" };
        var all = UserOwnedModel.RlsTables(db.Model)
            .Select(t => t.PostgresTableName)
            .ToList();

        foreach (var t in all.Where(t => !last.Contains(t, StringComparer.Ordinal))) yield return t;
        foreach (var t in last.Where(t => all.Contains(t, StringComparer.Ordinal))) yield return t;
    }
}
