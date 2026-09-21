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
            if (!await TableExistsAsync(db, table, ct)) continue; // creating migration not yet applied
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
            if (!await TableExistsAsync(db, table, ct)) continue; // creating migration not yet applied
            await db.Database.ExecuteSqlRawAsync(
                $"DELETE FROM \"{table}\" WHERE \"UserId\" = ANY({{0}})", [ids], ct);
        }
    }

    /// <summary>
    /// True if <paramref name="table"/> physically exists. A user-owned entity can land in
    /// the EF model (and therefore in <see cref="UserOwnedModel.RlsTables"/>) before its
    /// creating migration is applied — same transitional state <c>RlsParityStartupCheck</c>
    /// already tolerates. Without this check, every test's teardown would hard-fail the
    /// moment such an entity is added, regardless of whether that test touches it.
    /// </summary>
    private static async Task<bool> TableExistsAsync(AppDbContext db, string table, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT to_regclass(@qualified) IS NOT NULL";
        var p = cmd.CreateParameter();
        p.ParameterName = "qualified";
        p.Value = $"public.\"{table}\"";
        cmd.Parameters.Add(p);
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }

    /// <summary>
    /// Deletes test users the suite abandoned, and every row they own.
    ///
    /// The premise this method was FIRST written on was wrong, and the wrong version
    /// shipped: it deleted rows whose UserId had no AspNetUsers row, on the assumption
    /// that suites delete their user and strand the rows. Measured on 2026-08-24, of
    /// 14,378 categories in the shared test database exactly 0 were orphaned that way —
    /// 26 belonged to the sentinel and 14,352 to 579 test users that were never deleted
    /// at all. The sweep was a no-op by construction and silently reported nothing,
    /// because most suites do not delete their user in the first place.
    ///
    /// So the unit of leakage is the USER, not the orphaned row. Every test email in the
    /// suite ends in @example.com, @example.invalid, or a .local domain (verified against
    /// both the sources and all 579 leftover rows — the three patterns cover every one,
    /// with nothing outside them). Deleting those users and the rows they own is what
    /// actually reclaims the space.
    ///
    /// The sentinel is unreachable here by construction rather than by exclusion: it has
    /// no AspNetUsers row by design, and this method only ever deletes users it finds in
    /// AspNetUsers. Its fixtures — 26 seeded categories and the three 10000000-… Accounts
    /// no migration recreates — are therefore never candidates.
    /// </summary>
    public static async Task<int> SweepAbandonedTestUsersAsync(
        AppDbContext db, CancellationToken ct = default)
    {
        var fixturesBefore = await CountSentinelFixturesAsync(db, Sentinel, ct);

        var abandoned = await db.Users.IgnoreQueryFilters()
            .Where(u => u.Email!.EndsWith("@example.com")
                     || u.Email!.EndsWith("@example.invalid")
                     || u.Email!.EndsWith(".local"))
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (abandoned.Count == 0) return 0;

        await PurgeUsersAsync(db, abandoned, ct);
        var deleted = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"AspNetUsers\" WHERE \"Id\" = ANY({0})", [abandoned.ToArray()], ct);

        var fixturesAfter = await CountSentinelFixturesAsync(db, Sentinel, ct);
        if (fixturesAfter < fixturesBefore)
        {
            throw new InvalidOperationException(
                $"Test-user sweep destroyed sentinel fixtures ({fixturesBefore} -> {fixturesAfter}). "
                + "These are seeded test data with no AspNetUsers row by design; the fixture "
                + "Accounts in particular are restorable from no migration. Restore the test "
                + "database before running anything else.");
        }

        return deleted;
    }

    /// <summary>Fixture owner. Has no AspNetUsers row by design — see the class remarks.</summary>
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Sentinel-owned Categories + Accounts — the fixtures a sweep must never touch.</summary>
    private static async Task<int> CountSentinelFixturesAsync(
        AppDbContext db, Guid sentinel, CancellationToken ct)
    {
        var categories = await db.Categories.IgnoreQueryFilters()
            .CountAsync(c => c.UserId == sentinel, ct);
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .CountAsync(a => a.UserId == sentinel, ct);
        return categories + accounts;
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
