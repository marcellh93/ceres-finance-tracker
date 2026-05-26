using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

[PreAuthScope]
public sealed class TotpReplayGuard
{
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _hasher;

    // Per-user semaphore: serializes concurrent TryAcceptAsync calls for the same user
    // within this app process. Without serialization, two simultaneous requests with the
    // same valid TOTP code would both read "no replay entries" and both succeed.
    // SemaphoreSlim(1,1) is a non-reentrant mutex. The ConcurrentDictionary is safe to
    // share across request scopes because TotpReplayGuard is registered as Scoped but
    // the dictionary itself is held in a static field (process-lifetime).
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    public TotpReplayGuard(AppDbContext db, Argon2idPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>
    /// Returns true if the code has not been accepted within the replay window for
    /// this user; false if it is a replay. On true, the code is recorded in the table
    /// and old rows beyond the window are purged.
    ///
    /// Concurrency safety: a per-user SemaphoreSlim serializes concurrent calls within
    /// this process. Without serialization, two simultaneous requests with the same valid
    /// code would both read "no replay entries" and both succeed, defeating the guard.
    /// </summary>
    public async Task<bool> TryAcceptAsync(Guid userId, string code, CancellationToken ct)
    {
        var sem = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            return await TryAcceptLockedAsync(userId, code, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<bool> TryAcceptLockedAsync(Guid userId, string code, CancellationToken ct)
    {
        var threshold = DateTime.UtcNow - MfaConstants.ReplayWindow;

        // Stage 9.6.1 (2026-05-18) — wrap the read+write sequence in an explicit
        // transaction with SET LOCAL app.current_user_ref. Without this, the
        // [PreAuthCallSite("Auth.LoginTotp")] context causes the RLS interceptor
        // to RESET the GUC on every pooled-connection acquisition; the SELECT
        // would return zero candidates and the INSERT would fail with
        // `42501 new row violates row-level security policy`. The transaction
        // pins one connection for the duration so the GUC persists across both
        // reads and writes.
        await using var scope = await _db.BeginPreAuthUserScopeAsync(userId, ct);

        // Cross-tenant by design: called during MFA step before full identity cookie is issued; userId comes from the MFA-pending cookie claim. Stage 10 architecture test allow-lists this file.
        var candidates = await _db.TotpReplayEntries
            .IgnoreQueryFilters()
            .Where(e => e.UserId == userId && e.AcceptedAt > threshold)
            .ToListAsync(ct);

        foreach (var row in candidates)
        {
            var result = _hasher.VerifyHashedPassword(new ApplicationUser(), row.CodeHash, code);
            if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
            {
                return false;   // replay; scope rolls back on dispose
            }
        }

        // Insert + opportunistic purge of out-of-window entries.
        _db.TotpReplayEntries.Add(new TotpReplayEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = _hasher.HashPassword(new ApplicationUser(), code),
            AcceptedAt = DateTime.UtcNow,
        });
        // Cross-tenant by design: purge of expired replay entries; retention sweep operates across all users. Stage 10 architecture test allow-lists this file.
        await _db.TotpReplayEntries
            .IgnoreQueryFilters()
            .Where(e => e.AcceptedAt < threshold)
            .ExecuteDeleteAsync(ct);
        await _db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return true;
    }
}
