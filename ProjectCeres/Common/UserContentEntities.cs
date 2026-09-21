using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>
/// Single source of truth for "the user's own content" — the entities a data export
/// enumerates and (Stage 13.9) an erasure deletes. = FinanceTables + the support
/// tables (FinanceTables excludes SupportTickets for a legacy sentinel-remap reason,
/// not a content reason; support correspondence IS user content). Excludes all
/// security/auth-internal tables (audit, failed-login, sessions, tokens).
/// </summary>
public static class UserContentEntities
{
    private static readonly HashSet<string> SupportContentTables =
        new(StringComparer.Ordinal) { "SupportTickets", "SupportMessages", "SupportTicketAttachments" };

    public static IReadOnlyList<UserOwnedTable> List(IReadOnlyModel model)
    {
        var finance = UserOwnedModel.FinanceTables(model).ToList();
        var have = finance.Select(t => t.PostgresTableName).ToHashSet(StringComparer.Ordinal);
        var support = UserOwnedModel.RlsTables(model)
            .Where(t => SupportContentTables.Contains(t.PostgresTableName) && !have.Contains(t.PostgresTableName));
        return finance.Concat(support)
            .OrderBy(t => t.PostgresTableName, StringComparer.Ordinal).ToList();
    }
}
