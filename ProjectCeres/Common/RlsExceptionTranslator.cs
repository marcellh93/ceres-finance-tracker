using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Common;

/// <summary>
/// Translates Postgres <c>SqlState 42501</c> on a user-owned table into a typed
/// <see cref="RlsPolicyViolationException"/>. Stage 7.6.2 / ADR-0068.
///
/// <para>
/// Used by <c>AppDbContext.SaveChangesAsync</c> via a catch block. EF Core wraps
/// command-level exceptions in <see cref="DbUpdateException"/> before they reach
/// the SaveChanges caller, and the <c>ISaveChangesInterceptor</c> /
/// <c>IDbCommandInterceptor</c> hooks are observational only — they cannot replace
/// the propagating exception. The catch-and-rethrow override at the DbContext
/// boundary is the documented mechanism for transforming exceptions.
/// </para>
///
/// <para>
/// Other 42501 cases (missing GRANT on a non-user-owned table; read-only table)
/// pass through unchanged — only the user-owned-table case is wrapped.
/// </para>
/// </summary>
public static class RlsExceptionTranslator
{
    /// <summary>
    /// Inspects the <see cref="DbUpdateException"/> chain for a Postgres 42501 on a
    /// user-owned table. If found, builds an <see cref="RlsPolicyViolationException"/>;
    /// otherwise returns <c>false</c> and the original exception is left to propagate.
    /// </summary>
    /// <param name="userOwnedTableNames">The set of user-owned Postgres table names —
    /// supplied by the caller from <c>UserOwnedModel.RlsTables(db.Model)</c> (Stage 9.5b).</param>
    public static bool TryTranslate(
        DbUpdateException original,
        ICurrentUserAccessor user,
        IReadOnlySet<string> userOwnedTableNames,
        out RlsPolicyViolationException? translated)
    {
        translated = null;

        var pg = FindPostgresException(original);
        if (pg is null || pg.SqlState != "42501")
            return false;

        // Postgres does NOT populate the structured TableName field on RLS WITH CHECK
        // violations (verified against PG 16; the table name lives only in the message
        // text). Fall back to message parsing: "new row violates row-level security
        // policy for table \"<TableName>\"".
        var tableName = ExtractTableNameFromMessage(pg.MessageText) ?? pg.TableName;
        if (string.IsNullOrEmpty(tableName) || !userOwnedTableNames.Contains(tableName))
            return false;

        var gucUserId = user.UserId == Guid.Empty ? (Guid?)null : user.UserId;
        translated = new RlsPolicyViolationException(tableName, attemptedUserId: null, gucUserId, pg);
        return true;
    }

    private static string? ExtractTableNameFromMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        // Match `for table "<name>"` (RLS message) or `for relation "<name>"` (GRANT message).
        var match = System.Text.RegularExpressions.Regex.Match(
            message,
            "for (?:table|relation) \"(?<name>[^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static PostgresException? FindPostgresException(Exception ex)
    {
        for (var current = (Exception?)ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg)
                return pg;
        }
        return null;
    }
}
