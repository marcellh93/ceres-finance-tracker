using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>
/// A user-owned table and the concrete entity it maps to. Movement is the abstract TPC
/// root (no table); the three concrete subtypes (Transactions, Transfers,
/// LiabilityPayments) each carry their own entry.
/// </summary>
public sealed record UserOwnedTable(string PostgresTableName, Type EntityType);

/// <summary>
/// Single source of truth for the set of user-owned tables, derived from the EF
/// model (Stage 9.5b) — replaces the hand-typed UserOwnedTables.All. A table is
/// user-owned iff its entity type implements <see cref="IUserOwned"/>, is concrete,
/// and maps to a physical table. The abstract TPC root Movement is excluded; its
/// three concrete subtypes are included via their own table names.
/// </summary>
public static class UserOwnedModel
{
    private static readonly HashSet<string> AuthInternalTables = new(StringComparer.Ordinal)
    {
        "UserSessions", "UserBlockedIps", "UserMfaBackupCodes", "TotpReplayEntries",
        "PasswordResetTokens", "EmailChangeTokens", "LockoutUnlockTokens",
        "AuditLogs", "EmailConfirmationTokens",
        // SupportTickets: created in Stage 12.5, long after the Phase 1/2 sentinel
        // era, so it can never hold sentinel rows for the dev-seed remap to move.
        "SupportTickets",
    };

    /// <summary>Every user-owned table that must carry an RLS policy.</summary>
    public static IReadOnlyList<UserOwnedTable> RlsTables(IReadOnlyModel model) =>
        model.GetEntityTypes()
            .Where(e => !e.ClrType.IsAbstract
                        && typeof(IUserOwned).IsAssignableFrom(e.ClrType)
                        && e.GetTableName() is not null)
            .Select(e => new UserOwnedTable(e.GetTableName()!, e.ClrType))
            .GroupBy(t => t.PostgresTableName)
            .Select(g => g.First())
            .OrderBy(t => t.PostgresTableName, StringComparer.Ordinal)
            .ToList();

    /// <summary>Finance+attachment subset the dev-seed tool remaps — no auth-internal tables.</summary>
    public static IReadOnlyList<UserOwnedTable> FinanceTables(IReadOnlyModel model) =>
        RlsTables(model).Where(t => !AuthInternalTables.Contains(t.PostgresTableName)).ToList();
}
