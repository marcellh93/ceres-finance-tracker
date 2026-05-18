using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class MfaBackupCodeService
{
    // Per-user semaphore: serializes concurrent VerifyAndConsumeAsync calls for the same user
    // within this app process. Without serialization, two simultaneous requests with the same
    // valid backup code would both read "unused row exists" and both succeed, defeating the
    // single-use guarantee. Mirrors TotpReplayGuard._userLocks (Stage 6b.3 Gap 1).
    //
    // In-process locking is single-host only. Multi-host fix (e.g. ConcurrencyStamp on
    // UserMfaBackupCode) is captured in planning-phase3.md § Stage 6b.2 deferred decisions.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _hasher;

    public MfaBackupCodeService(AppDbContext db, Argon2idPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<string>> GenerateAndPersistAsync(Guid userId, CancellationToken ct)
    {
        var codes = new List<string>(MfaConstants.BackupCodeBatchSize);
        for (int i = 0; i < MfaConstants.BackupCodeBatchSize; i++)
        {
            var raw = GenerateOne();
            var hash = _hasher.HashPassword(new ApplicationUser(), raw);
            _db.UserMfaBackupCodes.Add(new UserMfaBackupCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = hash,
                CreatedAt = DateTime.UtcNow,
            });
            codes.Add(Format(raw));
        }
        await _db.SaveChangesAsync(ct);
        return codes;
    }

    public async Task<bool> VerifyAndConsumeAsync(
        Guid userId, string submittedCode, string clientIp, CancellationToken ct)
    {
        var sem = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            return await VerifyAndConsumeLockedAsync(userId, submittedCode, clientIp, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<bool> VerifyAndConsumeLockedAsync(
        Guid userId, string submittedCode, string clientIp, CancellationToken ct)
    {
        var normalized = NormalizeForVerify(submittedCode);
        if (normalized is null) return false;

        // Cross-tenant by design: called during MFA step before full identity cookie is issued; userId comes from the MFA-pending cookie claim. Stage 10 architecture test allow-lists this file.
        var unused = await _db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .ToListAsync(ct);

        foreach (var row in unused)
        {
            var result = _hasher.VerifyHashedPassword(new ApplicationUser(), row.CodeHash, normalized);
            if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
            {
                row.UsedAt = DateTime.UtcNow;
                row.UsedFromIp = clientIp;
                await _db.SaveChangesAsync(ct);
                return true;
            }
        }
        return false;
    }

    public async Task<IReadOnlyList<string>> RegenerateAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: called via authenticated session; userId from session claim. Stage 10 allow-lists this file.
        await _db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        return await GenerateAndPersistAsync(userId, ct);
    }

    /// <summary>
    /// Stage 9.6 — Deletes all persisted backup codes for the user without generating new ones.
    /// Called from `POST /api/auth/mfa/disable` so that turning MFA off invalidates any codes
    /// the user still has on paper. If the user re-enrols later, fresh codes are generated.
    /// </summary>
    public async Task PurgeAsync(Guid userId, CancellationToken ct)
    {
        // Cross-tenant by design: called via authenticated session; userId from session claim. Stage 10 allow-lists this file.
        await _db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }

    private static string GenerateOne()
    {
        var sb = new StringBuilder(MfaConstants.BackupCodeLength);
        Span<byte> bytes = stackalloc byte[MfaConstants.BackupCodeLength];
        RandomNumberGenerator.Fill(bytes);
        for (int i = 0; i < MfaConstants.BackupCodeLength; i++)
        {
            sb.Append(MfaConstants.CrockfordAlphabet[bytes[i] & 0x1F]);
        }
        return sb.ToString();
    }

    /// <summary>Inserts hyphens every 4 chars: ABCD-EFGH-JKLM-NPQR.</summary>
    private static string Format(string raw) =>
        $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}-{raw[12..16]}";

    private static string? NormalizeForVerify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var stripped = input.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        return MfaConstants.BackupCodeShape.IsMatch(stripped) ? stripped : null;
    }
}
