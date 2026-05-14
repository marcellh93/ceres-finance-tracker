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
/// <para>
/// SET LOCAL runs as a separate command via <c>connection.CreateCommand()</c> sharing
/// the EF command's transaction, NOT prepended into <c>command.CommandText</c> — the
/// prepend pattern misaligns Npgsql's per-statement rows-affected array on writes and
/// raises <c>DbUpdateConcurrencyException</c>. See Npgsql/efcore.pg #2412.
/// </para>
///
/// <para>
/// Async interception methods <c>await ExecuteNonQueryAsync</c>; sync paths call the
/// sync variant. Mixing sync IO into async EF pipelines starves the thread pool under
/// any request load.
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

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SetUserGucAsync(command, cancellationToken).ConfigureAwait(false);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        SetUserGuc(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SetUserGucAsync(command, cancellationToken).ConfigureAwait(false);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        SetUserGuc(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SetUserGucAsync(command, cancellationToken).ConfigureAwait(false);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private void SetUserGuc(DbCommand command)
    {
        if (!TryBuildSetLocalCommand(command, out var setLocal))
            return;

        using (setLocal)
        {
            setLocal.ExecuteNonQuery();
        }
    }

    private async ValueTask SetUserGucAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!TryBuildSetLocalCommand(command, out var setLocal))
            return;

#if NET8_0_OR_GREATER
        await using (setLocal.ConfigureAwait(false))
#else
        using (setLocal)
#endif
        {
            await setLocal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryBuildSetLocalCommand(DbCommand command, out DbCommand setLocal)
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
            setLocal = null!;
            return false;
        }

        var connection = command.Connection
            ?? throw new InvalidOperationException("DbCommand has no Connection when interceptor fired.");

        setLocal = connection.CreateCommand();
        setLocal.Transaction = command.Transaction;
        setLocal.CommandText = $"SET LOCAL \"{GucName}\" = '{userId:D}'";
        return true;
    }
}
