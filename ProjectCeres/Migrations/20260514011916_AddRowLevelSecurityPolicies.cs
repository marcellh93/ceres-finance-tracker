using Microsoft.EntityFrameworkCore.Migrations;
using ProjectCeres.Common;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 7.5 / ADR-0068 — installs PostgreSQL Row-Level Security on every user-owned
    /// table.
    ///
    /// <para>
    /// Three phases inside one <c>Up()</c>:
    /// </para>
    /// <list type="number">
    ///   <item><b>Phase A.</b> Attachment-table schema: add nullable <c>UserId</c>,
    ///         backfill from parent, set NOT NULL, add index. Must run BEFORE Phase B
    ///         because the policies on <c>TransactionAttachments</c> and
    ///         <c>TransferAttachments</c> reference <c>UserId</c>.</item>
    ///   <item><b>Phase B.</b> Enable RLS + FORCE RLS + add <c>user_isolation</c>
    ///         policy on all 24 user-owned tables (iterated from
    ///         <see cref="UserOwnedTables.All"/>).</item>
    ///   <item><b>Phase C.</b> Verify <c>ceres_admin</c> retains <c>BYPASSRLS</c>;
    ///         <c>RAISE EXCEPTION</c> if not — a defensive idempotency check.</item>
    /// </list>
    ///
    /// <para>
    /// <c>Down()</c> drops the policies and disables RLS. Phase A's schema additions
    /// (the <c>UserId</c> columns) are NOT reversed — standard EF practice; down
    /// migrations don't drop data.
    /// </para>
    /// </summary>
    public partial class AddRowLevelSecurityPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ----------------------------------------------------------------
            // Phase A — attachment-table UserId schema migration.
            // ----------------------------------------------------------------

            migrationBuilder.Sql(@"
                ALTER TABLE ""TransactionAttachments"" ADD COLUMN IF NOT EXISTS ""UserId"" uuid NULL;

                UPDATE ""TransactionAttachments"" a
                SET ""UserId"" = t.""UserId""
                FROM ""Transactions"" t
                WHERE a.""TransactionId"" = t.""Id"" AND a.""UserId"" IS NULL;

                ALTER TABLE ""TransactionAttachments"" ALTER COLUMN ""UserId"" SET NOT NULL;

                CREATE INDEX IF NOT EXISTS ""IX_TransactionAttachments_UserId""
                  ON ""TransactionAttachments"" (""UserId"");
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""TransferAttachments"" ADD COLUMN IF NOT EXISTS ""UserId"" uuid NULL;

                UPDATE ""TransferAttachments"" a
                SET ""UserId"" = t.""UserId""
                FROM ""Transfers"" t
                WHERE a.""TransferId"" = t.""Id"" AND a.""UserId"" IS NULL;

                ALTER TABLE ""TransferAttachments"" ALTER COLUMN ""UserId"" SET NOT NULL;

                CREATE INDEX IF NOT EXISTS ""IX_TransferAttachments_UserId""
                  ON ""TransferAttachments"" (""UserId"");
            ");

            // ----------------------------------------------------------------
            // Phase B — enable RLS + user_isolation policy on every user-owned table.
            //
            // current_setting('app.current_user_ref', true) returns NULL when the GUC
            // is unset (the `true` is missing_ok). The policy then evaluates to NULL,
            // which is treated as false — filtering all rows. This is the "fail closed"
            // property: a leaked scope or a missed SET LOCAL returns zero rows, never
            // someone else's rows. (See ADR-0068 § Rationale.)
            // ----------------------------------------------------------------

            foreach (var table in UserOwnedTables.All)
            {
                // NULLIF(..., '') guards against Postgres's `DISCARD ALL` behavior on
                // pooled connection return — DISCARD ALL resets custom GUCs to their
                // default value (empty string, not NULL). `current_setting(..., true)`
                // on a never-set GUC returns NULL, but on a "reset" GUC returns ''.
                // Without the NULLIF, the `::uuid` cast raises 22P02 on the empty string;
                // with it, both cases collapse to NULL and the policy evaluates to false
                // (zero rows / WITH CHECK fail) — the fail-closed property holds.
                migrationBuilder.Sql($@"
                    ALTER TABLE ""{table.PostgresTableName}"" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE ""{table.PostgresTableName}"" FORCE ROW LEVEL SECURITY;

                    DROP POLICY IF EXISTS user_isolation ON ""{table.PostgresTableName}"";
                    CREATE POLICY user_isolation ON ""{table.PostgresTableName}""
                      USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
                      WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
                ");
            }

            // ----------------------------------------------------------------
            // Phase C — defensive check: ceres_admin retains BYPASSRLS.
            // ----------------------------------------------------------------

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ceres_admin') THEN
                        RAISE EXCEPTION 'ceres_admin role does not exist. Run scripts/setup-postgres-roles.sql.';
                    END IF;
                    IF NOT (SELECT rolbypassrls FROM pg_roles WHERE rolname = 'ceres_admin') THEN
                        RAISE EXCEPTION 'ceres_admin role exists but does not have BYPASSRLS. Run scripts/setup-postgres-roles.sql.';
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop policies in reverse order. The attachment UserId columns are NOT
            // reversed: down migrations don't drop data (standard EF practice).
            foreach (var table in UserOwnedTables.All)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS user_isolation ON ""{table.PostgresTableName}"";
                    ALTER TABLE ""{table.PostgresTableName}"" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE ""{table.PostgresTableName}"" DISABLE ROW LEVEL SECURITY;
                ");
            }
        }
    }
}
