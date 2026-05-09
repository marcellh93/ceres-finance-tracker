using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class TotpReplayGuard
{
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _hasher;

    public TotpReplayGuard(AppDbContext db, Argon2idPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    /// <summary>
    /// Returns true if the code has not been accepted within the replay window for
    /// this user; false if it is a replay. On true, the code is recorded in the table
    /// and old rows beyond the window are purged.
    /// </summary>
    public async Task<bool> TryAcceptAsync(Guid userId, string code, CancellationToken ct)
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
