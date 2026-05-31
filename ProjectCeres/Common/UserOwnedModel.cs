using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

// The UserOwnedTable record type is declared in UserOwnedTables.cs (the hand-list this
// helper supersedes). Reused here to avoid a duplicate type during the 9.5b cutover;
// it moves into this file when UserOwnedTables.cs is deleted (Task 6).

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
    };

    /// <summary>Every user-owned table that must carry an RLS policy.</summary>
    public static IReadOnlyList<UserOwnedTable> RlsTables(IModel model) =>
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
    public static IReadOnlyList<UserOwnedTable> FinanceTables(IModel model) =>
        RlsTables(model).Where(t => !AuthInternalTables.Contains(t.PostgresTableName)).ToList();
}
