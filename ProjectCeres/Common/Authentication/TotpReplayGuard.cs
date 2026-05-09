using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

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

        var candidates = await _db.TotpReplayEntries
            .Where(e => e.UserId == userId && e.AcceptedAt > threshold)
            .ToListAsync(ct);

        foreach (var row in candidates)
        {
            var result = _hasher.VerifyHashedPassword(new ApplicationUser(), row.CodeHash, code);
            if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
            {
                return false;   // replay
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
        await _db.TotpReplayEntries
            .Where(e => e.AcceptedAt < threshold)
            .ExecuteDeleteAsync(ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
