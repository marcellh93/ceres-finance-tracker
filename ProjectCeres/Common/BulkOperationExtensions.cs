using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Common;

/// <summary>
/// Stage 7.6.3 — wrappers around <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync{TSource}"/>
/// and <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TSource}"/> that
/// throw <see cref="AffectedRowCountMismatchException"/> when the affected row count
/// differs from the caller's expectation.
///
/// <para>
/// Use these at call sites where a silent zero-row outcome would be a regression — typically
/// "consume this specific token row by Id" style operations after we've already verified
/// the row exists. A future "GUC didn't get set" bug would otherwise return 0 rows
/// silently and the caller would behave as if the consume succeeded.
/// </para>
///
/// <para>
/// Do NOT use these on supersede sweeps, retention sweeps, or session-revoke calls where
/// 0 rows is a legitimate outcome.
/// </para>
/// </summary>
public static class BulkOperationExtensions
{
    public static async Task<int> ExecuteUpdateExactlyAsync<TSource>(
        this IQueryable<TSource> source,
        Action<UpdateSettersBuilder<TSource>> setters,
        int expectedRows = 1,
        CancellationToken ct = default,
        [CallerMemberName] string callSite = "")
    {
        var affected = await source.ExecuteUpdateAsync(setters, ct);
        if (affected != expectedRows)
        {
            throw new AffectedRowCountMismatchException(
                expected: expectedRows,
                actual: affected,
                operation: nameof(ExecuteUpdateExactlyAsync),
                callSite: callSite);
        }
        return affected;
    }

    public static async Task<int> ExecuteDeleteExactlyAsync<TSource>(
        this IQueryable<TSource> source,
        int expectedRows = 1,
        CancellationToken ct = default,
        [CallerMemberName] string callSite = "")
    {
        var affected = await source.ExecuteDeleteAsync(ct);
        if (affected != expectedRows)
        {
            throw new AffectedRowCountMismatchException(
                expected: expectedRows,
                actual: affected,
                operation: nameof(ExecuteDeleteExactlyAsync),
                callSite: callSite);
        }
        return affected;
    }
}
