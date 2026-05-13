using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common;

/// <summary>
/// Sets the per-command PostgreSQL GUC <c>app.current_user_ref</c> from
/// <see cref="ICurrentUserAccessor"/> immediately before each EF command runs.
/// PostgreSQL RLS policies (Stage 7.5, ADR-0068) read that GUC to filter rows
/// per-user.
///
/// <para>
/// SET LOCAL is transaction-scoped, so it does not leak across pooled connections.
/// On Guid.Empty (pre-auth) the interceptor never throws — it silently skips the
/// SET LOCAL (RLS policies treat an unset GUC as zero rows via current_setting(..., true)).
/// On Guid.Empty outside the documented pre-auth call sites it logs a warning so
/// regressions are loud in dev/log scanning.
/// </para>
///
/// Registered only on AppDbContext (the runtime, RLS-bound context). AdminDbContext
/// uses the ceres_admin role which has BYPASSRLS — the SET LOCAL would be redundant
/// there and is intentionally not registered.
/// </summary>
public sealed class RowLevelSecurityInterceptor(
    ICurrentUserAccessor user,
    IPreAuthCallSiteTagger preAuth,
    ILogger<RowLevelSecurityInterceptor> logger)
    : DbCommandInterceptor
{
    private const string GucName = "app.current_user_ref";

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        SetUserGuc(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        SetUserGuc(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        SetUserGuc(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SetUserGuc(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        SetUserGuc(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        SetUserGuc(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void SetUserGuc(DbCommand command)
    {
        var userId = user.UserId;
        if (userId == Guid.Empty)
        {
            if (!preAuth.IsLegitimatePreAuth())
            {
                logger.LogWarning(
                    "DB command issued with no resolved user (Guid.Empty) outside a tagged pre-auth call site. " +
                    "RLS policies will evaluate to zero rows. Command: {CommandText}",
                    command.CommandText);
            }
            return;
        }

        // Prepend SET LOCAL to the same DbCommand so it runs in the same transaction
        // and against the same connection that EF is about to execute. Using a single
        // batched command avoids opening a separate connection that might not share
        // the pooled transaction context.
        command.CommandText =
            $"SET LOCAL \"{GucName}\" = '{userId:D}'; " + command.CommandText;
    }
}
