using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Orchestrates the lockout self-service unlock flow per
/// docs/superpowers/specs/2026-05-11-stage-6-10-lockout-self-service-unlock-design.md.
///
/// IssueAsync is called by AuthController.Login on the lockout transition only (the
/// failing PasswordSignInAsync that flipped LockoutEnd null → not null). One token
/// per lockout window prevents email-flood DoS.
///
/// ConfirmAsync clears AccessFailedCount + LockoutEnd, consumes the token, writes
/// the audit row. No MFA gate, no session revocation, no SecurityStamp regen —
/// the unlock is an undo of a failed-login side effect, not a credential change.
/// </summary>
public class LockoutUnlockService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly LockoutUnlockTokenGenerator _tokens;
    private readonly IEmailService _email;
    private readonly IEmailComposer _composer;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly ILogger<LockoutUnlockService> _logger;
    private readonly IAuditLogWriter _auditLog;
    private readonly LockoutCache _lockoutCache;

    public LockoutUnlockService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        LockoutUnlockTokenGenerator tokens,
        IEmailService email,
        IEmailComposer composer,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        ILogger<LockoutUnlockService> logger,
        IAuditLogWriter auditLog,
        LockoutCache lockoutCache)
    {
        _userManager = userManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _email = email;
        _composer = composer;
        _recipients = recipients;
        _languages = languages;
        _logger = logger;
        _auditLog = auditLog;
        _lockoutCache = lockoutCache;
    }

    public virtual async Task IssueAsync(
        Guid userId, string userEmail, string ip, string userAgent,
        string unlockUrlBase, CancellationToken ct)
    {
        var sem = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            // Cross-tenant by design: issued for a locked-out (unauthenticated) user; caller is not in session. Stage 10 allow-lists this file.
            await _db.LockoutUnlockTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);
            var now = DateTime.UtcNow;
            _db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = now + TokenLifetime,
                ConsumedAt = null,
            });
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/app/lockout-unlock#token={rawToken}";

        try
        {
            var recipient = await _recipients.ResolveAsync(userId, ct);
            var culture = await _languages.ResolveForUserAsync(userId, ct);
            var msg = _composer.Compose(EmailTemplateKey.LockoutUnlock, culture, unlockUrl, ip)
                with { To = recipient };
            await _email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            // Per spec: failed sends are logged but never roll back the token-issue.
            _logger.LogError(ex, "Failed to send lockout-unlock email; token row already committed.");
        }
    }

    public async Task<LockoutUnlockOutcome> ConfirmAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _argon.RunDummyHash();
            return new LockoutUnlockOutcome.InvalidToken();
        }

        var now = DateTime.UtcNow;
        // Cross-tenant by design: scans unconsumed tokens across all users to verify by hash before the caller is identified. Stage 10 architecture test allow-lists this file.
        var candidates = await _db.LockoutUnlockTokens
            .IgnoreQueryFilters()
            .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        LockoutUnlockToken? match = null;
        foreach (var candidate in candidates)
        {
            if (_tokens.Verify(rawToken, candidate.TokenHash))
            {
                match = candidate;
                break;
            }
        }

        if (match is null)
        {
            // Constant-time: even with zero candidates, run one verify so timing doesn't reveal "no rows".
            if (candidates.Count == 0) _argon.RunDummyHash();
            return new LockoutUnlockOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Cross-tenant by design: re-read inside lock; user identity resolved from token row, not HTTP cookie. Stage 10 architecture test allow-lists this file.
            var current = await _db.LockoutUnlockTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= DateTime.UtcNow)
            {
                return new LockoutUnlockOutcome.InvalidToken();
            }

            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is null)
            {
                return new LockoutUnlockOutcome.InvalidToken();
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await _userManager.SetLockoutEndDateAsync(user, null);

            // Stage 9.1.5.b: invalidate the LockoutCache hint so OnRejected doesn't surface
            // a stale "locked" envelope on the user's next request burst. The DB row is now
            // unlocked; the cache must follow.
            if (!string.IsNullOrEmpty(user.Email)) _lockoutCache.Remove(user.Email);

            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            // Stage 7.6.3: Exactly-1 — the row was just re-read inside the lock and verified
            // unconsumed; a 0-row outcome means GUC drift or concurrent supersede and must
            // fail loud rather than silently report "unlock succeeded".
            await _db.LockoutUnlockTokens
                .IgnoreQueryFilters()
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateExactlyAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct: ct);

            await _auditLog.RecordAsync(user.Id, AuditLogAction.LockoutSelfServiceUnlock, ct: ct);

            return new LockoutUnlockOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

}
