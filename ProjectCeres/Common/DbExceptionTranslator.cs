using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Common;

/// <summary>
/// Translates the three Postgres SqlState codes that surface in user-facing flows into
/// typed exceptions. Stage 7.6.5.
///
/// <para>
/// Used by <c>AppDbContext.SaveChangesAsync</c> via a catch block, after
/// <see cref="RlsExceptionTranslator"/>. EF Core wraps command-level exceptions in
/// <see cref="DbUpdateException"/> before they reach the SaveChanges caller, and the
/// <c>ISaveChangesInterceptor</c> / <c>IDbCommandInterceptor</c> hooks are observational
/// only — they cannot replace the propagating exception. The catch-and-rethrow override
/// at the DbContext boundary is the documented mechanism for transforming exceptions
/// (lesson confirmed in 7.6.2).
/// </para>
///
/// <para>
/// Empirical PG 18.3 probe (Stage 7.6.5, against <c>project_ceres_test</c>) confirmed
/// that all three handled SqlStates populate the structured fields cleanly:
/// </para>
/// <list type="table">
///   <item><term>23505 (unique)</term><description>TableName, ConstraintName populated</description></item>
///   <item><term>23503 (foreign key)</term><description>TableName, ConstraintName populated</description></item>
///   <item><term>23502 (NOT NULL)</term><description>TableName, ColumnName populated</description></item>
/// </list>
/// <para>
/// Unlike RLS 42501 (handled by <see cref="RlsExceptionTranslator"/>) which leaves
/// <c>TableName</c> empty for WITH CHECK violations and required message-text
/// parsing, these three codes don't need a fallback path. The translator still
/// guards against null fields per the Postgres protocol-docs warning ("frontends
/// should not assume the presence of any of these fields") so a future code path
/// emitting these SqlStates without the structured fields still gets the typed
/// exception.
/// </para>
/// </summary>
public static class DbExceptionTranslator
{
    public static bool TryTranslate(DbUpdateException original, out InvalidOperationException? translated)
    {
        translated = null;

        var pg = FindPostgresException(original);
        if (pg is null) return false;

        switch (pg.SqlState)
        {
            case "23505":
                translated = new UniqueConstraintViolationException(
                    NullIfEmpty(pg.TableName), NullIfEmpty(pg.ConstraintName), pg);
                return true;
            case "23503":
                translated = new ForeignKeyViolationException(
                    NullIfEmpty(pg.TableName), NullIfEmpty(pg.ConstraintName), pg);
                return true;
            case "23502":
                translated = new NullConstraintViolationException(
                    NullIfEmpty(pg.TableName), NullIfEmpty(pg.ColumnName), pg);
                return true;
            default:
                return false;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

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
