using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Orchestrates the password-reset flow per docs/superpowers/specs/2026-05-10-password-reset-design.md.
/// Per-user semaphore serialises the token supersede+insert. Per-email rate gate is service-side
/// (MemoryCache); per-IP gate is the existing AuthLoginByIp policy applied at the controller.
/// </summary>
[PreAuthScope]
public sealed class PasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    // Stage 9.6.1 (2026-05-19) — ConfirmAsync needs to look up the token row by
    // TokenLookup BEFORE the userId is known, but the user_isolation RLS policy
    // filters every row when app.current_user_ref is unset (PreAuth context).
    // The chicken-and-egg is solved by reading via AdminDbContext (ceres_admin,
    // BYPASSRLS) just for the initial lookup; once we have match.UserId we open
    // a normal PreAuthUserScope on _db (ceres_app) for all subsequent writes.
    // Same admin-read-then-scoped-write pattern as Stage 7.6.5's IUserJobRunner.
    private readonly AdminDbContext _admin;
    private readonly Argon2idPasswordHasher _argon;
    private readonly PasswordResetTokenGenerator _tokens;
    private readonly TokenLookupHasher _lookupHasher;
    private readonly TotpReplayGuard _replayGuard;
    private readonly FailedLoginRecorder _failedLogins;
    private readonly IEmailService _email;
    private readonly IEmailComposer _composer;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordResetService> _logger;
    private readonly IAuditLogWriter _auditLog;
    private readonly ILookupNormalizer _normalizer;
    private readonly TimeProvider _timeProvider;

    public PasswordResetService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        AdminDbContext admin,
        Argon2idPasswordHasher argon,
        PasswordResetTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        TotpReplayGuard replayGuard,
        FailedLoginRecorder failedLogins,
        IEmailService email,
        IEmailComposer composer,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        IMemoryCache cache,
        ILogger<PasswordResetService> logger,
        IAuditLogWriter auditLog,
        ILookupNormalizer normalizer,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _admin = admin;
        _argon = argon;
        _tokens = tokens;
        _lookupHasher = lookupHasher;
        _replayGuard = replayGuard;
        _failedLogins = failedLogins;
        _email = email;
        _composer = composer;
        _recipients = recipients;
        _languages = languages;
        _cache = cache;
        _logger = logger;
        _auditLog = auditLog;
        _normalizer = normalizer;
        _timeProvider = timeProvider;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Password reset rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    [RlsBypassJustified("CER-1011")]
    public async Task RequestAsync(
        string email, string ip, string userAgent, string resetUrlBase, CancellationToken ct)
    {
        // Stage 9.1.5.b Task 6 follow-up: route through ILookupNormalizer so this site
        // tracks the project's normalizer registration. If the registration changes
        // (e.g. back to Identity's default UpperInvariantLookupNormalizer), the
        // FindByEmailAsync lookup below — and the FailedLoginRecorder cache key downstream
        // (itself fixed in 4b35911 to use ILookupNormalizer) — would otherwise silently
        // miss their UserManager-normalized rows. Null-coalesce fallback preserves the
        // empty-string contract for the early-return branch immediately below.
        var trimmed = email?.Trim();
        var normalized = _normalizer.NormalizeEmail(trimmed) ?? trimmed ?? "";
        if (normalized.Length == 0)
        {
            _argon.RunDummyHash();
            return;
        }

        // Per-email rate gate (5/hour). Holds counters in MemoryCache keyed by email.
        // Throws RateLimitedException; controller maps to 429.
        EnforceEmailRateLimit(normalized);

        var user = await _userManager.FindByEmailAsync(normalized);

        // Stage 6.16: equalise the dominant Argon2id cost across known/unknown branches.
        // The known branch pays TWO Argon2id hashes: one here for the FindByEmail-result
        // matching cost, and one downstream when `_tokens.Hash(rawToken)` writes the
        // PasswordResetToken row. The unknown branch must mirror both to close the
        // ≈150ms timing channel an attacker would otherwise use to enumerate registered
        // email addresses.
        _argon.RunDummyHash();

        if (user is null)
        {
            // Stage 6.16: mirror the second `_tokens.Hash(rawToken)` Argon2id cost the
            // known branch pays at line 113. Without this, an attacker can distinguish
            // known vs unknown emails by a single Argon2id worth of wall-clock time.
            _argon.RunDummyHash();
            await _failedLogins.RecordAsync(
                normalized, null, FailedLoginReason.PasswordResetUnknownEmail, ip, userAgent, ct);
            return;
        }

        var sem = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            // Stage 9.6.1 (2026-05-18) — wrap the supersede-UPDATE + INSERT in a
            // PreAuthUserScope. Pre-auth path so the RLS interceptor RESETs the
            // GUC on every connection-acquisition; the transaction pins one
            // connection so the SET LOCAL persists across both operations.
            await using var scope = await _db.BeginPreAuthUserScopeAsync(user.Id, ct);

            // Supersede prior unused tokens.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);
            var lookup = _lookupHasher.ComputeLookup(rawToken);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenLookup = lookup,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = now + TokenLifetime,
                ConsumedAt = null,
                MfaVerifiedAt = null,
            });
            await _db.SaveChangesAsync(ct);
            await scope.CommitAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var resetUrl = $"{resetUrlBase.TrimEnd('/')}/app/password-reset#token={rawToken}";

        try
        {
            var recipient = await _recipients.ResolveAsync(user.Id, ct);
            var culture = await _languages.ResolveForUserAsync(user.Id, ct);
            var msg = _composer.Compose(EmailTemplateKey.PasswordResetRequest, culture, resetUrl)
                with { To = recipient };
            await _email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            // Per spec: failed sends are logged but never block the user-facing request.
            _logger.LogError(ex, "Failed to send password-reset email; token row already committed.");
        }

        await _auditLog.RecordAsync(user.Id, AuditLogAction.PasswordResetRequested, ct: ct);
    }

    private void EnforceEmailRateLimit(string normalizedEmail)
    {
        // Sliding-window-ish: if the cached counter for this email reaches the limit,
        // reject. Cache TTL = the window length, so the bucket auto-resets.
        // No per-email semaphore: MemoryCache is thread-safe, and a tiny race on Count
        // is intentional — accuracy here is best-effort; the per-IP limiter is the hard
        // defence against abuse. Keying by arbitrary email is safe because we never hold
        // a lock on the string (no unbounded ConcurrentDictionary growth).
        var key = $"pwreset:rate:{normalizedEmail}";
        var entry = _cache.Get<RateBucket>(key);
        if (entry is null || entry.WindowStart + EmailRateWindow <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            entry = new RateBucket { WindowStart = _timeProvider.GetUtcNow().UtcDateTime, Count = 1 };
            _cache.Set(key, entry, EmailRateWindow);
            return;
        }

        if (entry.Count >= EmailRateLimit)
        {
            var elapsed = _timeProvider.GetUtcNow().UtcDateTime - entry.WindowStart;
            var remaining = (int)Math.Ceiling((EmailRateWindow - elapsed).TotalSeconds);
            throw new RateLimitedException(Math.Max(remaining, 1));
        }

        entry.Count += 1;
        _cache.Set(key, entry, entry.WindowStart + EmailRateWindow - _timeProvider.GetUtcNow().UtcDateTime);
    }

    private sealed class RateBucket
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }

    [RlsBypassJustified("CER-1012")]
    public async Task<PasswordResetConfirmOutcome> ConfirmAsync(
        string rawToken, string newPassword, string? totpCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            // Constant-time: still pay one Argon2 verify against a dummy hash.
            _argon.RunDummyHash();
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        // Stage 6.15: O(1) indexed lookup via HMAC-derived TokenLookup column.
        // Replaces the candidate-loop pattern that ran Argon2id verify against every
        // unconsumed unexpired row (Argon2id-amplification DoS on /confirm).
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Stage 9.6.1 (2026-05-19): the UserId isn't known yet, so we cannot set
        // app.current_user_ref before this SELECT. Without the GUC set, the
        // user_isolation RLS policy on ceres_app would filter every row. Read
        // via AdminDbContext (ceres_admin, BYPASSRLS) for this initial lookup
        // only; once we have match.UserId we open a PreAuthUserScope on _db
        // for all subsequent writes. Same pattern as Stage 7.6.5's IUserJobRunner.
        // IgnoreQueryFilters is required: AdminDbContext bypasses RLS at the DB
        // level (Postgres role) but inherits AppDbContext's EF-level per-user
        // global filter, which would still hide the row since _currentUser.UserId
        // is empty under [PreAuthCallSite].
        var match = await _admin.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(t => t.TokenLookup == lookup
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            // Constant-time floor: pay one Argon2 verify against a dummy hash so a miss
            // doesn't reveal "no matching lookup" via timing.
            _argon.RunDummyHash();
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        // Defence-in-depth: the unique TokenLookup index already pins the match, but
        // still verify the Argon2id-hashed TokenHash. If the hash check fails, the row
        // was tampered with — reject without consuming.
        if (!_tokens.Verify(rawToken, match.TokenHash))
        {
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Stage 9.6.1 (2026-05-19): now that match.UserId is known, open a
            // PreAuthUserScope on _db (ceres_app) so all the writes below pass
            // the user_isolation RLS policy. The scope wraps everything inside
            // the lock and commits at the end; on any early return or exception
            // the DisposeAsync rolls back.
            await using var rlsScope = await _db.BeginPreAuthUserScopeAsync(match.UserId, ct);

            // Re-read the token row inside the lock; another concurrent caller may have consumed it.
            // Cross-tenant by design: still pre-auth at this point; user id not yet in cookie. Stage 10 architecture test allow-lists this file.
            var current = await _admin.PasswordResetTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                return new PasswordResetConfirmOutcome.InvalidToken();
            }

            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is null)
            {
                return new PasswordResetConfirmOutcome.InvalidToken();
            }

            // MFA gate: if user has TwoFactorEnabled, a totpCode is required.
            var mfaVerified = false;
            if (user.TwoFactorEnabled)
            {
                if (string.IsNullOrWhiteSpace(totpCode))
                {
                    return new PasswordResetConfirmOutcome.RequiresTotp();
                }

                // Backup codes are NOT accepted at reset time per ADR-0069.
                // The reset endpoint accepts only six-digit authenticator-app codes.
                if (!MfaConstants.TotpCodeShape.IsMatch(totpCode))
                {
                    return new PasswordResetConfirmOutcome.InvalidTotp();
                }

                var ok = await _userManager.VerifyTwoFactorTokenAsync(
                    user, Microsoft.AspNetCore.Identity.TokenOptions.DefaultAuthenticatorProvider, totpCode);
                if (!ok)
                {
                    return new PasswordResetConfirmOutcome.InvalidTotp();
                }

                var accepted = await _replayGuard.TryAcceptAsync(user.Id, totpCode, ct);
                if (!accepted)
                {
                    return new PasswordResetConfirmOutcome.InvalidTotp();
                }

                mfaVerified = true;
            }

            // Password write: remove + add (re-runs all Identity password validators).
            var remove = await _userManager.RemovePasswordAsync(user);
            if (!remove.Succeeded)
            {
                return new PasswordResetConfirmOutcome.PasswordPolicyViolation(remove.Errors.ToList());
            }
            var add = await _userManager.AddPasswordAsync(user, newPassword);
            if (!add.Succeeded)
            {
                // Token NOT consumed; user can retry with a different password.
                return new PasswordResetConfirmOutcome.PasswordPolicyViolation(add.Errors.ToList());
            }

            // Promote EmailConfirmed (the email itself is proof of address ownership).
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            // Mark token consumed; include MfaVerifiedAt when MFA was satisfied.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            if (mfaVerified)
            {
                await _db.PasswordResetTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.Id == match.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime)
                        .SetProperty(t => t.MfaVerifiedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);
            }
            else
            {
                await _db.PasswordResetTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.Id == match.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);
            }

            // Bulk-revoke all sessions for this user.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.UserSessions
                .IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            // Cancel any pending email-change for this user. Reasoning: a password reset
            // is itself a recovery/compromise signal. If an attacker initiated /email-change/request
            // before being reset out, leaving the verify token live for up to 30 minutes would let
            // them complete the takeover after the legitimate user resets. Stage 6.12.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            var hadPendingEmailChange = await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .AnyAsync(t => t.UserId == user.Id && t.ConsumedAt == null, ct);
            if (hadPendingEmailChange)
            {
                await _db.EmailChangeTokens
                    .IgnoreQueryFilters()
                    .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

                try
                {
                    var recipient = await _recipients.ResolveAsync(user.Id, ct);
                    var culture = await _languages.ResolveForUserAsync(user.Id, ct);
                    var msg = _composer.Compose(EmailTemplateKey.PasswordResetCancelledEmailChange, culture)
                        with { To = recipient };
                    await _email.SendAsync(msg, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email-change-cancelled-by-password-reset email.");
                }
            }

            // SecurityStamp regen — invalidates any in-flight Identity cookies via SecurityStampValidator.
            await _userManager.UpdateSecurityStampAsync(user);

            // Clear lockout if any.
            await _userManager.ResetAccessFailedCountAsync(user);
            await _userManager.SetLockoutEndDateAsync(user, null);

            // Clear any half-authenticated MFA-pending cookie on the caller's browser.
            await _signInManager.SignOutAsync();

            // Notification email — never blocks the return.
            try
            {
                var recipient = await _recipients.ResolveAsync(user.Id, ct);
                var culture = await _languages.ResolveForUserAsync(user.Id, ct);
                var msg = _composer.Compose(EmailTemplateKey.PasswordChanged, culture)
                    with { To = recipient };
                await _email.SendAsync(msg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password-changed notification email.");
            }

            await _auditLog.RecordAsync(user.Id, AuditLogAction.PasswordResetCompleted, ct: ct);

            // Commit the PreAuthUserScope's transaction (and SET LOCAL GUC) once
            // every RLS-scoped write above has persisted. DisposeAsync at end of
            // `using` will be a no-op if commit already ran.
            await rlsScope.CommitAsync(ct);

            return new PasswordResetConfirmOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

}
