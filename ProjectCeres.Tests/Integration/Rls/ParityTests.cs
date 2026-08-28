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

    /// <summary>
    /// Every user_isolation policy must actually compare UserId against the GUC, with the
    /// fail-closed NULLIF guard, on BOTH the read and write clause.
    ///
    /// The other parity tests match on policy NAME only. A policy called user_isolation
    /// with a typo'd GUC name, a missing NULLIF, or no WITH CHECK sits on the table, keeps
    /// relforcerowsecurity true, and passes both of them while isolating nothing. Each new
    /// table's policy is hand-copied into a fresh migration, so that is a live risk rather
    /// than a theoretical one.
    /// </summary>
    [Fact]
    public async Task Every_user_isolation_policy_compares_UserId_to_the_guc_fail_closed()
    {
        await using var admin = _fixture.CreateAdminContext();
        var conn = admin.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        var broken = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT tablename, COALESCE(qual,''), COALESCE(with_check,'')
                                FROM pg_policies WHERE policyname = 'user_isolation' ORDER BY tablename";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var (table, using_, withCheck) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
                foreach (var (clause, text) in new[] { ("USING", using_), ("WITH CHECK", withCheck) })
                {
                    if (!text.Contains("\"UserId\"", StringComparison.Ordinal))
                        broken.Add($"{table}: {clause} does not reference UserId");
                    else if (!text.Contains("app.current_user_ref", StringComparison.Ordinal))
                        broken.Add($"{table}: {clause} does not read app.current_user_ref");
                    else if (!text.Contains("NULLIF", StringComparison.OrdinalIgnoreCase))
                        broken.Add($"{table}: {clause} lacks the NULLIF fail-closed guard — an empty GUC would raise 22P02 instead of denying");
                }
            }
        }

        broken.Should().BeEmpty(
            "a user_isolation policy that does not compare UserId to the GUC isolates nothing, "
            + "yet passes every name-based parity check");
    }

    /// <summary>
    /// The attachment FK must be scoped to the ticket's owner IN THE DATABASE, not merely
    /// in the EF model.
    ///
    /// Postgres runs FK checks and ON DELETE CASCADE through a referential-integrity
    /// trigger that RLS does not apply to. A single-column FK therefore let user B attach
    /// to user A's ticket, and A deleting their own ticket destroyed B's row — both
    /// reproduced against this database on 2026-08-23. The composite key is what makes
    /// that unrepresentable.
    ///
    /// The model-level test in UserOwnedModelTests cannot stand in for this one: the
    /// child's HasPrincipalKey induces the principal key in the runtime model whether or
    /// not HasAlternateKey is declared, so a model assertion passes on configurations
    /// that would not produce the constraint. This reads pg_constraint.
    /// </summary>
    [Theory]
    [InlineData("SupportTicketAttachments", "SupportMessageId")]
    [InlineData("TransactionAttachments", "TransactionId")]
    [InlineData("TransferAttachments", "TransferId")]
    public async Task Attachment_fks_are_scoped_to_the_parent_owner_in_the_database(
        string table, string parentColumn)
    {
        await using var admin = _fixture.CreateAdminContext();
        var conn = admin.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        var p = cmd.CreateParameter();
        p.ParameterName = "table";
        p.Value = table;
        cmd.Parameters.Add(p);
        // conkey lists the referencing columns; confkey the referenced ones. Resolve both
        // to names so the assertion reads as the invariant rather than as column numbers.
        cmd.CommandText = @"
            SELECT c.conname,
                   (SELECT string_agg(a.attname, ',' ORDER BY x.ord)
                      FROM unnest(c.conkey) WITH ORDINALITY AS x(attnum, ord)
                      JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = x.attnum),
                   (SELECT string_agg(a.attname, ',' ORDER BY x.ord)
                      FROM unnest(c.confkey) WITH ORDINALITY AS x(attnum, ord)
                      JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = x.attnum),
                   c.confdeltype
              FROM pg_constraint c
             WHERE c.conrelid = quote_ident(@table)::regclass
               AND c.contype = 'f'";

        var found = new List<(string Name, string Cols, string RefCols, char OnDelete)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                found.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetChar(3)));
        }

        found.Should().ContainSingle($"{table} has exactly one foreign key, to its parent");
        var fk = found[0];

        fk.Cols.Should().Be($"{parentColumn},UserId",
            "a single-column FK lets an attachment hang off another user's ticket, which the "
            + "RLS-bypassing cascade then destroys");
        fk.RefCols.Should().Be("Id,UserId");
        fk.OnDelete.Should().Be('c', "cascade — an attachment cannot outlive its ticket");
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
