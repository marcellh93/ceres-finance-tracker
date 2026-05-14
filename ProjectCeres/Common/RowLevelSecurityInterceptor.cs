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
/// <b>Failure mode.</b> When <c>ICurrentUserAccessor.UserId == Guid.Empty</c>
/// (pre-auth requests: login, register, password-reset request, etc.), the
/// interceptor RESETs the GUC to its default (empty string in Postgres for
/// custom GUCs). The RLS policy uses <c>NULLIF(current_setting(...), '')</c> to
/// collapse both unset and reset-to-empty into NULL, then casts to uuid. NULL
/// comparisons evaluate to NULL/false in policy USING/WITH CHECK clauses, so
/// every row is filtered — the fail-closed property holds.
/// </para>
///
/// Registered only on AppDbContext (the runtime, RLS-bound context). AdminDbContext
/// uses the ceres_admin role which has BYPASSRLS — the GUC is irrelevant there
/// and is intentionally not registered.
/// </summary>
public sealed class RowLevelSecurityInterceptor(
    ICurrentUserAccessor user,
    IPreAuthCallSiteTagger preAuth,
    ILogger<RowLevelSecurityInterceptor> logger)
    : DbConnectionInterceptor
{
    private const string GucName = "app.current_user_ref";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetUserGuc(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await SetUserGucAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void SetUserGuc(DbConnection connection)
    {
        var userId = user.UserId;
        if (userId == Guid.Empty)
        {
            HandleEmptyUser(connection, async: false).GetAwaiter().GetResult();
            return;
        }

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT set_config('{GucName}', '{userId:D}', false)";
        cmd.ExecuteNonQuery();
    }

    private async ValueTask SetUserGucAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var userId = user.UserId;
        if (userId == Guid.Empty)
        {
            await HandleEmptyUser(connection, async: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT set_config('{GucName}', '{userId:D}', false)";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// On Guid.Empty (pre-auth requests, or the rare background path with no user
    /// resolved yet) we still issue <c>RESET</c> to clear any leaked GUC from a
    /// previous pool user. Npgsql's default <c>DISCARD ALL</c> on connection return
    /// would handle this, but RESET is belt-and-braces: the property still holds if
    /// someone ever sets <c>No Reset On Close=true</c> (required for pgBouncer
    /// transaction mode). Logs a warning when the call site isn't tagged pre-auth.
    /// </summary>
    private async ValueTask HandleEmptyUser(
        DbConnection connection, bool async, CancellationToken cancellationToken = default)
    {
        if (!preAuth.IsLegitimatePreAuth())
        {
            logger.LogWarning(
                "DB connection opened with no resolved user (Guid.Empty) outside a tagged pre-auth call site. " +
                "RLS policies will evaluate to zero rows for every command on this connection.");
        }

        if (async)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"RESET \"{GucName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"RESET \"{GucName}\"";
            cmd.ExecuteNonQuery();
        }
    }
}
