using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 — the parity test. Connects to the actual database and
/// queries <c>pg_policies</c>; asserts the set of tables with a <c>user_isolation</c>
/// policy installed matches <see cref="UserOwnedModel.RlsTables"/> exactly (Stage 9.5b:
/// the model-derived set, closing the Hole B blind spot where the parity oracle derived
/// its expectation from the same hand-list a missing entity was absent from). Fails the
/// build if a new <see cref="IUserOwned"/> entity ships without an accompanying
/// RLS policy in a migration.
/// </summary>
[Collection("RlsTests")]
public class ParityTests
{
    private readonly RlsTestFixture _fixture;

    public ParityTests(RlsTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Names any migration that is committed but not applied to this database.
    ///
    /// Without this, a missing policy reads identically in two opposite situations:
    /// the author forgot to write the SQL, or the SQL exists and the database has
    /// not run it. The advice differs completely — write a migration vs apply one —
    /// and the second is the normal state on every entity+migration slice.
    ///
    /// Reads __EFMigrationsHistory directly rather than GetPendingMigrationsAsync:
    /// this fixture hands out an AdminDbContext, which carries no migrations
    /// assembly, so the EF API reports zero pending and the hint never fires.
    /// </summary>
    private static async Task<string> PendingMigrationHintAsync(DbContext ctx)
    {
        try
        {
            var applied = new HashSet<string>(StringComparer.Ordinal);
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT ""MigrationId"" FROM ""__EFMigrationsHistory""";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) applied.Add(reader.GetString(0));
            }

            var onDisk = typeof(AppDbContext).Assembly.GetTypes()
                .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
                .Where(id => id is not null)
                .Select(id => id!)
                .ToList();

            var pending = onDisk.Where(id => !applied.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (pending.Count == 0) return "";

            return $" NOTE: {pending.Count} migration(s) are committed but NOT applied to this "
                 + $"database ({string.Join(", ", pending)}). Run `dotnet ef database update` before "
                 + "reading this failure as missing SQL — the policy may already be written.";
        }
        catch (Exception ex)
        {
            // Surface the reason rather than hiding it: a silent empty hint is how
            // this helper failed the first time it was written.
            return $" (pending-migration check unavailable: {ex.GetType().Name})";
        }
    }


    [Fact]
    public async Task UserOwnedModel_RlsTables_match_pg_policies_user_isolation_set()
    {
        await using var admin = _fixture.CreateAdminContext();

        var conn = admin.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tablename FROM pg_policies WHERE policyname = 'user_isolation' ORDER BY tablename";

        var installed = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                installed.Add(reader.GetString(0));
        }

        var expected = UserOwnedModel.RlsTables(admin.Model).Select(t => t.PostgresTableName).OrderBy(n => n).ToList();
        var hint = await PendingMigrationHintAsync(admin);
        installed.Should().BeEquivalentTo(expected,
            "every user-owned entity must have an RLS user_isolation policy installed by a migration, "
            + "and no extra policies should exist." + hint);
    }

    [Fact]
    public async Task Every_user_owned_table_has_FORCE_RLS_enabled()
    {
        // FORCE ROW LEVEL SECURITY makes the policy apply even to the table owner —
        // critical because ceres_migrator (who creates tables) would otherwise bypass.
        await using var admin = _fixture.CreateAdminContext();

        var conn = admin.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public'
              AND c.relkind = 'r'
              AND c.relrowsecurity = true
              AND c.relforcerowsecurity = true
            ORDER BY c.relname";

        var forced = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                forced.Add(reader.GetString(0));
        }

        var expected = UserOwnedModel.RlsTables(admin.Model).Select(t => t.PostgresTableName).OrderBy(n => n).ToList();
        forced.Should().BeEquivalentTo(expected,
            "every user-owned table must have both ENABLE ROW LEVEL SECURITY and FORCE ROW LEVEL SECURITY");
    }

    [Fact]
    public async Task System_tables_have_no_user_isolation_policy()
    {
        // AspNetUsers, AccountTypes, CategoryTypes, Currencies, ReportTypes, Settings*lookups,
        // __EFMigrationsHistory — none should have user_isolation. The pre-auth login path
        // reads AspNetUsers; if it were RLS-bound, login itself would fail closed.
        await using var admin = _fixture.CreateAdminContext();

        var conn = admin.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        var systemTables = new[]
        {
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
            "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens",
            "AccountTypes", "CategoryTypes", "Currencies", "ReportTypes",
            "__EFMigrationsHistory",
        };

        foreach (var table in systemTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'user_isolation'";
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            count.Should().Be(0, $"system table {table} must not carry a user_isolation policy");
        }
    }
}
