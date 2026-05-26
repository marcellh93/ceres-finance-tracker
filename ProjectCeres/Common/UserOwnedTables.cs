using ProjectCeres.Models;

namespace ProjectCeres.Common;

/// <summary>
/// Single source of truth for the set of user-owned tables that carry both an EF
/// <c>HasQueryFilter</c> registration AND a PostgreSQL Row-Level Security policy
/// (Stage 7.5 / ADR-0068).
///
/// <para>
/// Three consumers iterate this list:
/// </para>
/// <list type="bullet">
///   <item><c>AppDbContext.OnModelCreating</c> registers a query filter per entity.</item>
///   <item>The <c>AddRowLevelSecurityPolicies</c> migration generates one
///         <c>ENABLE ROW LEVEL SECURITY</c> + <c>user_isolation</c> policy per table.</item>
///   <item>The parity architecture test queries <c>pg_policies</c> at runtime and
///         asserts the installed policies match this list exactly — failing the build
///         if a new user-owned entity ships without an RLS policy.</item>
/// </list>
///
/// <para>
/// Adding a new user-owned entity is one edit (append below); the filter, the migration
/// (for the next migration cycle), and the parity test all pick it up automatically.
/// </para>
///
/// <para>
/// <c>Movement</c> is the abstract TPC root — the runtime entity that carries the EF
/// query filter, but no Postgres table corresponds to it. RLS policies go on the three
/// concrete tables (<c>Transactions</c>, <c>Transfers</c>, <c>LiabilityPayments</c>).
/// </para>
/// </summary>
public sealed record UserOwnedTable(string PostgresTableName, Type EntityType);

public static class UserOwnedTables
{
    /// <summary>
    /// Every table that carries an RLS <c>user_isolation</c> policy. 24 entries.
    /// </summary>
    public static readonly IReadOnlyList<UserOwnedTable> All = new[]
    {
        // Finance domain (11)
        new UserOwnedTable("Accounts",                 typeof(Account)),
        new UserOwnedTable("Budgets",                  typeof(Budget)),
        new UserOwnedTable("Categories",               typeof(Category)),
        new UserOwnedTable("CategoryBudgets",          typeof(CategoryBudget)),
        new UserOwnedTable("ImportProfiles",           typeof(ImportProfile)),
        new UserOwnedTable("ImportStagedTransactions", typeof(ImportStagedTransaction)),
        new UserOwnedTable("ImportStagedTransfers",    typeof(ImportStagedTransfer)),
        new UserOwnedTable("ImportTransferExclusions", typeof(ImportTransferExclusion)),
        new UserOwnedTable("RecurringTransactions",    typeof(RecurringTransaction)),
        new UserOwnedTable("SavedReports",             typeof(SavedReport)),
        new UserOwnedTable("Settings",                 typeof(Settings)),

        // Movement TPC concrete tables (3) — no separate filter for Movement itself;
        // EF's TPC mapping copies the abstract root's query filter onto each concrete
        // entity, so HasQueryFilter on the three subtypes is implicit.
        new UserOwnedTable("Transactions",      typeof(Transaction)),
        new UserOwnedTable("Transfers",         typeof(Transfer)),
        new UserOwnedTable("LiabilityPayments", typeof(LiabilityPayment)),

        // Attachments (2) — gain UserId columns in the AddRowLevelSecurityPolicies
        // migration's Phase A. Stage 7.5 spec § 2.4 reconciliation.
        new UserOwnedTable("TransactionAttachments", typeof(TransactionAttachment)),
        new UserOwnedTable("TransferAttachments",    typeof(TransferAttachment)),

        // Auth-internal (8)
        new UserOwnedTable("UserSessions",        typeof(UserSession)),
        new UserOwnedTable("UserBlockedIps",      typeof(UserBlockedIp)),
        new UserOwnedTable("UserMfaBackupCodes",  typeof(UserMfaBackupCode)),
        new UserOwnedTable("TotpReplayEntries",   typeof(TotpReplayEntry)),
        new UserOwnedTable("PasswordResetTokens", typeof(PasswordResetToken)),
        new UserOwnedTable("EmailChangeTokens",   typeof(EmailChangeToken)),
        new UserOwnedTable("LockoutUnlockTokens", typeof(LockoutUnlockToken)),
        new UserOwnedTable("AuditLogs",           typeof(AuditLog)),
        new UserOwnedTable("EmailConfirmationTokens", typeof(EmailConfirmationToken)),
    };
}
