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
/// Orchestrates the email-address-change flow per docs/superpowers/specs/2026-05-10-stage-6-12-email-change-design.md.
/// Per-user semaphore serialises the dual-token supersede + insert. Per-new-email rate
/// gate is service-side (MemoryCache); per-IP gate is the existing AuthLoginByIp policy
/// applied to /confirm and /revoke at the controller. /request is reauth-gated, so it
/// inherits no extra per-IP rate limit beyond the global rate-limiter middleware.
/// </summary>
[PreAuthScope]
[RequiresAdminContext]
public sealed class EmailChangeService
{
    public static readonly TimeSpan VerifyTokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RevokeTokenLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    // Confirm/Revoke look up the token row by TokenLookup before the userId is
    // known; the user_isolation RLS policy on ceres_app filters every row when
    // app.current_user_ref is unset (PreAuth). Read via AdminDbContext (BYPASSRLS)
    // for the initial lookup, then open a PreAuthUserScope on _db once we have
    // match.UserId. Same pattern as PasswordResetService.ConfirmAsync.
    private readonly AdminDbContext _admin;
    private readonly Argon2idPasswordHasher _argon;
    private readonly EmailChangeTokenGenerator _tokens;
    private readonly TokenLookupHasher _lookupHasher;
    private readonly IEmailService _email;
    private readonly IEmailComposer _composer;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EmailChangeService> _logger;
    private readonly IAuditLogWriter _auditLog;
    private readonly ILookupNormalizer _normalizer;
    private readonly TimeProvider _timeProvider;

    public EmailChangeService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        AdminDbContext admin,
        Argon2idPasswordHasher argon,
        EmailChangeTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        IEmailService email,
        IEmailComposer composer,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        IMemoryCache cache,
        ILogger<EmailChangeService> logger,
        IAuditLogWriter auditLog,
        ILookupNormalizer normalizer,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _db = db;
        _admin = admin;
        _argon = argon;
        _tokens = tokens;
        _lookupHasher = lookupHasher;
        _email = email;
        _composer = composer;
        _recipients = recipients;
        _languages = languages;
        _cache = cache;
        _auditLog = auditLog;
        _logger = logger;
        _normalizer = normalizer;
        _timeProvider = timeProvider;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Email-change rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    [RlsBypassJustified("CER-1003")]
    public async Task<EmailChangeRequestOutcome> RequestAsync(
        Guid userId, string newEmail,
        string ip, string userAgent,
        string verifyUrlBase, string revokeUrlBase,
        CancellationToken ct)
    {
        // Stage 9.1.5.b Task 6 follow-up: route through ILookupNormalizer so this site
        // tracks the project's normalizer registration. If the registration changes
        // (e.g. back to Identity's default UpperInvariantLookupNormalizer), the
        // FindByEmailAsync lookup below would otherwise silently miss its UserManager-
        // normalized rows. Null-coalesce fallback preserves the empty-string contract.
        var trimmed = newEmail?.Trim();
        var normalized = _normalizer.NormalizeEmail(trimmed) ?? trimmed ?? "";
        if (normalized.Length == 0)
        {
            _argon.RunDummyHash();
            return new EmailChangeRequestOutcome.Accepted();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            // Stage 6.16 (corrected 2026-05-12 by count-based regression test):
            // equalise Argon2id cost across all branches. Unlike PasswordResetService,
            // EmailChangeService's happy path does NOT perform a FindByEmail-match
            // equalisation hash — the user lookup at line 81 is by UserId (no
            // Argon2id), so the happy path performs only TWO Argon2id calls (the
            // `_tokens.Hash(verifyRaw)` and `_tokens.Hash(revokeRaw)` calls at lines
            // 137–138). Each fast-return branch must mirror exactly two RunDummyHash
            // calls — not three. Three was the initial 6.16 fix; it created an
            // inverse timing channel (fast-returns ran 3x ≈450ms while happy path
            // ran 2x ≈300ms) which the Argon2id-count regression test now pins.
            _argon.RunDummyHash();
            _argon.RunDummyHash();
            return new EmailChangeRequestOutcome.Accepted();
        }

        if (string.Equals(user.NormalizedEmail, _userManager.NormalizeEmail(normalized),
                StringComparison.Ordinal))
        {
            // Stage 6.16: two RunDummyHash to mirror the happy-path's two `_tokens.Hash`
            // calls (Verify + Revoke). See comment on the user-null branch above.
            _argon.RunDummyHash();
            _argon.RunDummyHash();
            return new EmailChangeRequestOutcome.EmailUnchanged();
        }

        var existing = await _userManager.FindByEmailAsync(normalized);
        if (existing is not null)
        {
            // Stage 6.16: two RunDummyHash to mirror the happy-path Argon2id cost.
            // This branch is the highest-severity timing channel — an authenticated
            // user could otherwise enumerate other users' email addresses by timing
            // `/email-change/request` against guessed addresses.
            _argon.RunDummyHash();
            _argon.RunDummyHash();
            return new EmailChangeRequestOutcome.EmailAlreadyInUse();
        }

        EnforceEmailRateLimit(normalized);

        string verifyRaw, revokeRaw;
        var sem = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Supersede prior unused tokens (both Purposes).
            // Cross-tenant by design: RequestAsync runs under the authenticated user's HttpContext
            // but UserId is resolved via UserManager not the global filter. Stage 10 allow-lists this file.
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            verifyRaw = _tokens.Generate();
            revokeRaw = _tokens.Generate();
            var verifyHash = _tokens.Hash(verifyRaw);
            var revokeHash = _tokens.Hash(revokeRaw);
            var verifyLookup = _lookupHasher.ComputeLookup(verifyRaw);
            var revokeLookup = _lookupHasher.ComputeLookup(revokeRaw);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _db.EmailChangeTokens.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = EmailChangeTokenPurpose.VerifyNew,
                NewEmail = normalized,
                TokenLookup = verifyLookup,
                TokenHash = verifyHash,
                CreatedAt = now,
                ExpiresAt = now + VerifyTokenLifetime,
                ConsumedAt = null,
            });
            _db.EmailChangeTokens.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = EmailChangeTokenPurpose.RevokeOld,
                NewEmail = normalized,
                TokenLookup = revokeLookup,
                TokenHash = revokeHash,
                CreatedAt = now,
                ExpiresAt = now + RevokeTokenLifetime,
                ConsumedAt = null,
            });
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        // FIXME: re-surface in Stage 12 — no SPA route exists yet for either link target (roadmap Stage 12.8).
        var verifyUrl = $"{verifyUrlBase.TrimEnd('/')}/app/email-change/confirm#token={verifyRaw}";
        var revokeUrl = $"{revokeUrlBase.TrimEnd('/')}/app/email-change/revoke#token={revokeRaw}";

        try
        {
            var culture = await _languages.ResolveForUserAsync(user.Id, ct);
            var msg = _composer.Compose(EmailTemplateKey.EmailChangeVerifyNew, culture, verifyUrl)
                with { To = EmailRecipient.OverrideForEmailChange(normalized) };
            await _email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email-change verify email; token row already committed.");
        }

        try
        {
            var culture = await _languages.ResolveForUserAsync(user.Id, ct);
            var msg = _composer.Compose(EmailTemplateKey.EmailChangeRevokeOld, culture, normalized, revokeUrl)
                with { To = EmailRecipient.OverrideForEmailChange(user.Email!) };
            await _email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email-change revoke email; token row already committed.");
        }

        await _auditLog.RecordAsync(user.Id, AuditLogAction.EmailChangeRequested, ct: ct);

        return new EmailChangeRequestOutcome.Accepted();
    }

    [RlsBypassJustified("CER-1004")]
    public async Task<EmailChangeConfirmOutcome> ConfirmAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _argon.RunDummyHash();
            return new EmailChangeConfirmOutcome.InvalidToken();
        }

        // Stage 6.15: O(1) indexed lookup via HMAC-derived TokenLookup column.
        // Purpose filter is defence-in-depth — a raw token must never match across
        // purposes because each /request issues distinct VerifyNew + RevokeOld tokens.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Pre-auth lookup via AdminDbContext (BYPASSRLS) — userId not yet known, so the
        // ceres_app user_isolation policy would filter every row. Stage 10 allow-lists this file.
        var match = await _admin.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.TokenLookup == lookup
                     && t.Purpose == EmailChangeTokenPurpose.VerifyNew
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            _argon.RunDummyHash();
            return new EmailChangeConfirmOutcome.InvalidToken();
        }

        if (!_tokens.Verify(rawToken, match.TokenHash))
        {
            return new EmailChangeConfirmOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // userId now known: open a PreAuthUserScope on _db so the writes below pass user_isolation.
            await using var rlsScope = await _db.BeginPreAuthUserScopeAsync(match.UserId, ct);

            // Re-read inside lock via AdminDbContext (BYPASSRLS); another caller may have consumed it.
            var current = await _admin.EmailChangeTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                return new EmailChangeConfirmOutcome.InvalidToken();
            }

            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is null)
            {
                return new EmailChangeConfirmOutcome.InvalidToken();
            }

            // Re-check collision: another user may have grabbed the address between request and confirm.
            var collision = await _userManager.FindByEmailAsync(match.NewEmail);
            if (collision is not null && collision.Id != user.Id)
            {
                // Token NOT consumed — caller can /revoke to clean up.
                return new EmailChangeConfirmOutcome.EmailAlreadyInUse();
            }

            // Capture pre-mutation Email for the old-address notification at the end.
            var oldEmail = user.Email;

            // Update Identity row. SetEmailAsync handles Email + NormalizedEmail + EmailConfirmed=false reset
            // internally; we re-promote EmailConfirmed because the token itself is proof of address ownership.
            // SetUserNameAsync mirrors what AuthController.Register does (UserName == Email at registration).
            var setEmail = await _userManager.SetEmailAsync(user, match.NewEmail);
            if (!setEmail.Succeeded)
            {
                // Defensive: SetEmailAsync would only fail on validators or concurrency.
                return new EmailChangeConfirmOutcome.InvalidToken();
            }
            var setUserName = await _userManager.SetUserNameAsync(user, match.NewEmail);
            if (!setUserName.Succeeded)
            {
                return new EmailChangeConfirmOutcome.InvalidToken();
            }
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);

            // Consume matched VerifyNew row.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            // Stage 7.6.3: Exactly-1 — the row was just re-read inside the lock and verified
            // unconsumed; a 0-row outcome means GUC drift or concurrent supersede and must
            // fail loud rather than silently report "email change confirmed".
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateExactlyAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct: ct);

            // Consume sibling RevokeOld row.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user.Id
                         && t.Purpose == EmailChangeTokenPurpose.RevokeOld
                         && t.NewEmail == match.NewEmail
                         && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            // Bulk-revoke all sessions for this user.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.UserSessions
                .IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            // SecurityStamp regen — invalidates any in-flight Identity cookies via SecurityStampValidator.
            await _userManager.UpdateSecurityStampAsync(user);

            // Notifications — never block the return.
            // New address (now address-of-record): "your email was just changed to this address".
            try
            {
                var recipient = await _recipients.ResolveAsync(user.Id, ct);
                var culture = await _languages.ResolveForUserAsync(user.Id, ct);
                var msg = _composer.Compose(EmailTemplateKey.EmailChangeConfirmed, culture)
                    with { To = recipient };
                await _email.SendAsync(msg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email-change confirmed notification (new address).");
            }
            // Old address (per security-model.md "Notify the old address on successful
            // completion of the change"): closes the loop for the legitimate user who
            // may still control the old inbox.
            if (!string.IsNullOrEmpty(oldEmail))
            {
                try
                {
                    var culture = await _languages.ResolveForUserAsync(user.Id, ct);
                    var msg = _composer.Compose(EmailTemplateKey.EmailChangeConfirmedToOld, culture, oldEmail, match.NewEmail)
                        with { To = EmailRecipient.OverrideForEmailChange(oldEmail) };
                    await _email.SendAsync(msg, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email-change confirmed notification (old address).");
                }
            }

            await _auditLog.RecordAsync(match.UserId, AuditLogAction.EmailChangeConfirmed, ct: ct);

            await rlsScope.CommitAsync(ct);

            return new EmailChangeConfirmOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

    [RlsBypassJustified("CER-1005")]
    public async Task<EmailChangeRevokeOutcome> RevokeAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _argon.RunDummyHash();
            return new EmailChangeRevokeOutcome.InvalidToken();
        }

        // Stage 6.15: O(1) indexed lookup via HMAC-derived TokenLookup column.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Pre-auth lookup via AdminDbContext (BYPASSRLS) — userId not yet known. Stage 10 allow-lists this file.
        var match = await _admin.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.TokenLookup == lookup
                     && t.Purpose == EmailChangeTokenPurpose.RevokeOld
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            _argon.RunDummyHash();
            return new EmailChangeRevokeOutcome.InvalidToken();
        }

        if (!_tokens.Verify(rawToken, match.TokenHash))
        {
            return new EmailChangeRevokeOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // userId now known: open a PreAuthUserScope on _db so the consume write passes user_isolation.
            await using var rlsScope = await _db.BeginPreAuthUserScopeAsync(match.UserId, ct);

            // Re-read inside lock via AdminDbContext (BYPASSRLS); another caller may have consumed it.
            var current = await _admin.EmailChangeTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                return new EmailChangeRevokeOutcome.InvalidToken();
            }

            // Consume both sibling rows atomically (the matched RevokeOld + its VerifyNew sibling).
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == match.UserId
                         && t.NewEmail == match.NewEmail
                         && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            // Notify the OLD address only. The change is cancelled; user.Email is unchanged.
            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is not null)
            {
                try
                {
                    var recipient = await _recipients.ResolveAsync(user.Id, ct);
                    var culture = await _languages.ResolveForUserAsync(user.Id, ct);
                    var msg = _composer.Compose(EmailTemplateKey.EmailChangeRevokeNotificationToOld, culture)
                        with { To = recipient };
                    await _email.SendAsync(msg, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email-change revoke notification.");
                }
            }

            await _auditLog.RecordAsync(match.UserId, AuditLogAction.EmailChangeRevoked, ct: ct);

            await rlsScope.CommitAsync(ct);

            return new EmailChangeRevokeOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

    private void EnforceEmailRateLimit(string normalizedNewEmail)
    {
        // Sliding-window-ish: copy of PasswordResetService.EnforceEmailRateLimit with cache-key prefix swap.
        // No per-email semaphore: MemoryCache is thread-safe and a tiny race on Count is intentional.
        var key = $"emailchange:rate:{normalizedNewEmail}";
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

}
