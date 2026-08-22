using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectCeres.Migrations
{
    /// <summary>
    /// Stage 7 Task 15 (ADR-0066): one-shot remap of Phase 1/2 sentinel-tagged data onto
    /// the first registered real user. Runs inside a single PostgreSQL transaction with
    /// pre-checks (exactly one AspNetUsers row; at least one sentinel-tagged row exists)
    /// and a post-check (zero sentinel rows remain across every user-owned table). On
    /// pre-check failure: clean no-op. On post-check failure: raises an exception so the
    /// transaction rolls back.
    ///
    /// Down() is intentionally empty. Re-stamping the sentinel would destroy multi-user
    /// state if real users have registered since the remap; per project memory
    /// feedback_never_delete_db_without_consent, the recovery path is "restore from the
    /// pre-deploy snapshot", not auto-rollback.
    ///
    /// Auth tables (UserSession, AuditLog, PasswordResetToken, EmailChangeToken,
    /// LockoutUnlockToken, UserBlockedIp, UserMfaBackupCode, TotpReplayEntry,
    /// FailedLoginAttempt) are intentionally NOT touched — they were created after
    /// Stage 6 with real user ids and never held sentinel data.
    ///
    /// KNOWN SIDE EFFECT — duplicate categories (observed 2026-08-22).
    /// The pre-check only asserts that exactly one user exists; it does not check
    /// whether that user ALREADY has categories. If the user registered before this
    /// migration ran, CategorySeedService has already copied the 26 defaults to them
    /// with fresh GUIDs, and this remap then hands them the 26 sentinel-owned
    /// originals (fixed ids 20000000-…) as well — every default category appears
    /// twice. It happened on the dev database: the user registered 2026-05-16, the
    /// migration applied afterwards, and 26 duplicates showed up in the Categories UI.
    ///
    /// The transaction-carrying copy is always the sentinel one, because pre-existing
    /// Phase 1/2 transactions were already attached to those ids — so the runtime-seeded
    /// twins are unreferenced and safe to delete. Cleanup on dev was exactly that.
    ///
    /// This migration is one-shot and already applied everywhere it matters, so it will
    /// not recur on existing databases. It CAN recur on a database rebuilt from scratch
    /// if a user registers before `dotnet ef database update` runs. Migrate first, then
    /// register.
    /// </summary>
    public partial class RemapSentinelToFirstUser : Migration
    {
        private const string Sentinel = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
DO $$
DECLARE
    real_user_id uuid;
    user_count int;
    sentinel_row_count int;
BEGIN
    -- Pre-check 1: exactly one user, or no-op.
    SELECT COUNT(*) INTO user_count FROM ""AspNetUsers"";
    IF user_count = 0 THEN
        RAISE NOTICE 'Stage 7 Task 15 remap: zero registered users — no-op';
        RETURN;
    END IF;
    IF user_count > 1 THEN
        RAISE EXCEPTION 'Stage 7 Task 15 remap precondition failed: AspNetUsers contains % rows, expected exactly 1', user_count;
    END IF;

    SELECT ""Id"" INTO real_user_id FROM ""AspNetUsers"" LIMIT 1;

    -- Pre-check 2: at least one sentinel-tagged row exists somewhere, or no-op.
    sentinel_row_count :=
        (SELECT COUNT(*) FROM ""Accounts"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""Categories"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""Transactions"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""Transfers"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""LiabilityPayments"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""CategoryBudgets"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""Budgets"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""RecurringTransactions"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""SavedReports"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""ImportProfiles"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""ImportStagedTransactions"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""ImportStagedTransfers"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""ImportTransferExclusions"" WHERE ""UserId"" = '{Sentinel}') +
        (SELECT COUNT(*) FROM ""Settings"" WHERE ""UserId"" = '{Sentinel}');

    IF sentinel_row_count = 0 THEN
        RAISE NOTICE 'Stage 7 Task 15 remap: zero sentinel-tagged rows — no-op';
        RETURN;
    END IF;

    -- Remap step: explicit table list; no dynamic SQL. Each UPDATE rewrites only the
    -- rows currently carrying the sentinel UUID; rows already owned by a real user
    -- (created by registered users post-Stage-7) are untouched.
    UPDATE ""Accounts""                 SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""Categories""               SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""Transactions""             SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""Transfers""                SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""LiabilityPayments""        SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""CategoryBudgets""          SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""Budgets""                  SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""RecurringTransactions""    SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""SavedReports""             SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""ImportProfiles""           SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""ImportStagedTransactions"" SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""ImportStagedTransfers""    SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""ImportTransferExclusions"" SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';
    UPDATE ""Settings""                 SET ""UserId"" = real_user_id WHERE ""UserId"" = '{Sentinel}';

    -- Post-check: zero sentinel rows must remain across every user-owned table.
    -- If anything slipped through, raise to roll back the whole transaction.
    IF EXISTS (SELECT 1 FROM ""Accounts""                 WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""Categories""               WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""Transactions""             WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""Transfers""                WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""LiabilityPayments""        WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""CategoryBudgets""          WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""Budgets""                  WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""RecurringTransactions""    WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""SavedReports""             WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""ImportProfiles""           WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""ImportStagedTransactions"" WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""ImportStagedTransfers""    WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""ImportTransferExclusions"" WHERE ""UserId"" = '{Sentinel}') OR
       EXISTS (SELECT 1 FROM ""Settings""                 WHERE ""UserId"" = '{Sentinel}')
    THEN
        RAISE EXCEPTION 'Stage 7 Task 15 remap post-check failed: sentinel rows remain after UPDATE — rolling back transaction';
    END IF;

    RAISE NOTICE 'Stage 7 Task 15 remap: % sentinel rows moved to user %', sentinel_row_count, real_user_id;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. See class-level summary: the recovery path is to
            // restore from the pre-deploy snapshot, not to re-stamp the sentinel
            // (which would destroy multi-user state if real users have registered
            // since the remap ran).
        }
    }
}
