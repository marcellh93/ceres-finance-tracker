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
    private readonly TokenLookupHasher _lookupHasher;
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
        TokenLookupHasher lookupHasher,
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
        _lookupHasher = lookupHasher;
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
            // Stage 9.6.1 (2026-05-18) — wrap the supersede-UPDATE + INSERT in a
            // PreAuthUserScope so the user_isolation policy passes on both
            // operations. Pre-auth context (issued during a failed-login lockout
            // transition, no session cookie yet) → interceptor would otherwise
            // RESET the GUC and the writes would fail with 42501.
            await using var scope = await _db.BeginPreAuthUserScopeAsync(userId, ct);

            // Cross-tenant by design: issued for a locked-out (unauthenticated) user; caller is not in session. Stage 10 allow-lists this file.
            await _db.LockoutUnlockTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);
            var lookup = _lookupHasher.ComputeLookup(rawToken);
            var now = DateTime.UtcNow;
            _db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                // Stage 9.1.5.a — TokenLookup is NOT NULL + unique. IssueAsync MUST
                // stamp it to satisfy the schema contract. The ConfirmAsync read-side
                // refactor (O(N) → O(1) lookup) lands in Commit 2.
                TokenLookup = lookup,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = now + TokenLifetime,
                ConsumedAt = null,
            });
            await _db.SaveChangesAsync(ct);
            await scope.CommitAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var unlockUrl = $"{unlockUrlBase.TrimEnd('/')}/account/unlock#token={rawToken}";

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
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Cross-tenant by design: token-based pre-auth operation; caller is not in session.
        // Stage 10 architecture test allow-lists this file. Stage 9.1.5.a: indexed lookup
        // replaces the O(N) Argon2 scan; a single row matches the HMAC-derived TokenLookup
        // or none does, so we run at most one Argon2 verify per request.
        var candidate = await _db.LockoutUnlockTokens
            .IgnoreQueryFilters()
            .Where(t => t.TokenLookup == lookup && t.ConsumedAt == null && t.ExpiresAt > now)
            .SingleOrDefaultAsync(ct);

        if (candidate is null || !_tokens.Verify(rawToken, candidate.TokenHash))
        {
            // Constant-time: even with no matching row, run one verify so timing doesn't
            // reveal "no rows" vs "rows but no Argon2 match". Mirrors the prior pattern
            // and preserves the anti-enumeration property of the original scan.
            if (candidate is null) _argon.RunDummyHash();
            return new LockoutUnlockOutcome.InvalidToken();
        }

        var match = candidate;

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
