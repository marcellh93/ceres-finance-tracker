using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Stage 9.6.1 (2026-05-18) — opens a DB transaction and pins the PostgreSQL RLS
/// session GUC <c>app.current_user_ref</c> to the supplied <paramref name="userId"/>
/// for the lifetime of that transaction.
///
/// <para>
/// <b>Why this helper exists.</b> Stage 7.5 enabled <c>FORCE ROW LEVEL SECURITY</c>
/// with a <c>user_isolation</c> policy on every user-owned table. The policy
/// requires <c>app.current_user_ref</c> to match the row's <c>UserId</c> on both
/// <c>USING</c> (reads) and <c>WITH CHECK</c> (writes). The
/// <see cref="RowLevelSecurityInterceptor"/> RESETs the GUC on pre-auth call sites
/// (<see cref="UserContext.PreAuth"/>) — correct for unauthenticated reads, wrong
/// for the narrow case where a pre-auth path KNOWS the userId (from a pending-MFA
/// cookie, a password-reset email lookup, a lockout-trigger user record) and MUST
/// insert/update a row on that user's behalf. Pre-fix symptom:
/// <c>42501 new row violates row-level security policy</c> on the SaveChanges call —
/// surfaced 2026-05-18 from <see cref="TotpReplayGuard"/>.
/// </para>
///
/// <para>
/// <b>Why a transaction.</b> Npgsql pools connections per-command. Without an
/// explicit transaction, EF acquires a fresh pooled connection for every command
/// (each SELECT, ExecuteUpdateAsync, SaveChangesAsync), and the connection-open
/// interceptor RESETs the GUC on each acquisition — wiping any prior
/// <c>set_config(..., is_local := false)</c>. A first-attempt fix that called
/// set_config inside the service method failed for this reason. An explicit
/// transaction binds one connection for the duration of the scope so the GUC
/// (set with <c>SET LOCAL</c>) persists across every command inside it.
/// </para>
///
/// <para>
/// <b>Usage.</b> Wrap the entire pre-auth read+write sequence in the scope. The
/// scope commits on dispose unless <c>Rollback()</c> was called first.
/// </para>
///
/// <code>
/// await using (var scope = await _db.BeginPreAuthUserScopeAsync(userId, ct))
/// {
///     // SELECTs see the user's rows; INSERTs/UPDATEs pass WITH CHECK.
///     _db.PasswordResetTokens.Add(new ...);
///     await _db.SaveChangesAsync(ct);
///     await scope.CommitAsync(ct);
/// }
/// </code>
/// </summary>
public static class PreAuthRlsScope
{
    /// <summary>
    /// Opens a DB transaction on <paramref name="db"/> and sets
    /// <c>app.current_user_ref</c> = <paramref name="userId"/> within it
    /// (<c>SET LOCAL</c> scope, valid for the transaction's lifetime).
    /// Caller commits via <see cref="PreAuthUserScope.CommitAsync"/>; failing
    /// to commit before dispose rolls back.
    /// </summary>
    public static async Task<PreAuthUserScope> BeginPreAuthUserScopeAsync(
        this AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        // SET LOCAL persists for the transaction; the interceptor's RESET-on-open
        // doesn't fire again because the transaction holds the same connection.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_user_ref', {userId.ToString("D")}, true)",
            ct);
        return new PreAuthUserScope(tx);
    }
}

public sealed class PreAuthUserScope : IAsyncDisposable
{
    private readonly IDbContextTransaction _tx;
    private bool _committed;

    internal PreAuthUserScope(IDbContextTransaction tx)
    {
        _tx = tx;
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_committed) return;
        await _tx.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed)
        {
            await _tx.RollbackAsync();
        }
        await _tx.DisposeAsync();
    }
}
