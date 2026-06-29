using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common;

/// <summary>
/// Sets the PostgreSQL session GUC <c>app.current_user_ref</c> from
/// <see cref="ICurrentUserAccessor"/> every time a database connection is opened.
/// PostgreSQL RLS policies (Stage 7.5, ADR-0068) read that GUC to filter rows
/// per-user.
///
/// <para>
/// <b>Why connection-open, not per-command.</b> EF Core's bulk operations
/// <c>ExecuteDeleteAsync</c> and <c>ExecuteUpdateAsync</c> do not open an EF
/// transaction by default. <c>SET LOCAL</c> outside a transaction is a NOTICE and
/// a no-op (PostgreSQL silently ignores it). A per-command interceptor would
/// silently miss every bulk operation in production — token-consumption updates,
/// MFA cleanup, IP-block revocations — leaving them to run with the GUC unset
/// and the RLS policy filtering all rows. The connection-open hook fires once
/// per pooled-connection acquisition, before any command (bulk or otherwise) runs.
/// </para>
///
/// <para>
/// <b>Cross-request leak prevention.</b> The interceptor uses
/// <c>set_config(name, value, is_local=false)</c> for session scope. Npgsql's
/// default reset behavior (<c>DISCARD ALL</c> on connection return to the pool)
/// would clear the GUC anyway, but we explicitly <c>RESET</c> when no user is
/// resolved as belt-and-braces — and so the property holds even if a future
/// deployment disables Npgsql's reset (required for pgBouncer transaction mode).
/// </para>
///
/// <para>
/// Stage 7.6.7 / ADR-0073: switches on <see cref="UserContext"/> instead of
/// consulting the prior <c>IPreAuthCallSiteTagger</c> registry. Each case has a
/// distinct log signature so legitimate pre-auth quietly RESETs while
/// <c>Background</c> (the bug case — request reached the DB without auth or a
/// <c>[PreAuthCallSite]</c> tag) logs an error every time.
/// </para>
///
/// Registered only on AppDbContext (the runtime, RLS-bound context). AdminDbContext
/// uses the ceres_admin role which has BYPASSRLS — the GUC is irrelevant there
/// and is intentionally not registered.
/// </summary>
public sealed class RowLevelSecurityInterceptor(
    ICurrentUserAccessor user,
    ILogger<RowLevelSecurityInterceptor> logger)
    : DbConnectionInterceptor
{
    private const string GucName = "app.current_user_ref";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetUserGucSync(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await SetUserGucAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void SetUserGucSync(DbConnection connection)
    {
        switch (user.Context)
        {
            case UserContext.Resolved resolved:
                ExecuteSync(connection, $"SELECT set_config('{GucName}', '{resolved.UserId:D}', false)");
                return;

            case UserContext.PreAuth preAuth:
                logger.LogDebug("RLS GUC RESET on pre-auth call site {CallSite}.", preAuth.CallSite);
                ExecuteSync(connection, $"RESET \"{GucName}\"");
                return;

            case UserContext.Background background:
                logger.LogError(
                    "DB connection opened in Background context without a resolved user. " +
                    "Reason: {Reason}. RLS policies will evaluate to zero rows.",
                    background.Reason);
                ExecuteSync(connection, $"RESET \"{GucName}\"");
                return;

            case UserContext.Uninitialized:
                // Fires at EF model-creation time before any HTTP context exists. Logging here
                // would be noise — the model creator is supposed to be empty. Connection still
                // gets RESET so a leaked GUC from a prior pool user can't bleed through.
                ExecuteSync(connection, $"RESET \"{GucName}\"");
                return;
        }
    }

    private async Task SetUserGucAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        switch (user.Context)
        {
            case UserContext.Resolved resolved:
                await ExecuteAsync(connection, $"SELECT set_config('{GucName}', '{resolved.UserId:D}', false)", cancellationToken)
                    .ConfigureAwait(false);
                return;

            case UserContext.PreAuth preAuth:
                logger.LogDebug("RLS GUC RESET on pre-auth call site {CallSite}.", preAuth.CallSite);
                await ExecuteAsync(connection, $"RESET \"{GucName}\"", cancellationToken)
                    .ConfigureAwait(false);
                return;

            case UserContext.Background background:
                logger.LogError(
                    "DB connection opened in Background context without a resolved user. " +
                    "Reason: {Reason}. RLS policies will evaluate to zero rows.",
                    background.Reason);
                await ExecuteAsync(connection, $"RESET \"{GucName}\"", cancellationToken)
                    .ConfigureAwait(false);
                return;

            case UserContext.Uninitialized:
                // Fires at EF model-creation time before any HTTP context exists. Logging here
                // would be noise — the model creator is supposed to be empty. Connection still
                // gets RESET so a leaked GUC from a prior pool user can't bleed through.
                await ExecuteAsync(connection, $"RESET \"{GucName}\"", cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
    }

    private static void ExecuteSync(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
