using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
public sealed class EmailChangeService
{
    public static readonly TimeSpan VerifyTokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RevokeTokenLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly EmailChangeTokenGenerator _tokens;
    private readonly TokenLookupHasher _lookupHasher;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EmailChangeService> _logger;
    private readonly IAuditLogWriter _auditLog;

    public EmailChangeService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        EmailChangeTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        IEmailService email,
        IMemoryCache cache,
        ILogger<EmailChangeService> logger,
        IAuditLogWriter auditLog)
    {
        _userManager = userManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _lookupHasher = lookupHasher;
        _email = email;
        _cache = cache;
        _auditLog = auditLog;
        _logger = logger;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Email-change rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    public async Task<EmailChangeRequestOutcome> RequestAsync(
        Guid userId, string newEmail,
        string ip, string userAgent,
        string verifyUrlBase, string revokeUrlBase,
        CancellationToken ct)
    {
        var normalized = (newEmail ?? "").Trim().ToLowerInvariant();
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
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            verifyRaw = _tokens.Generate();
            revokeRaw = _tokens.Generate();
            var verifyHash = _tokens.Hash(verifyRaw);
            var revokeHash = _tokens.Hash(revokeRaw);
            var verifyLookup = _lookupHasher.ComputeLookup(verifyRaw);
            var revokeLookup = _lookupHasher.ComputeLookup(revokeRaw);

            var now = DateTime.UtcNow;
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

        var verifyUrl = $"{verifyUrlBase.TrimEnd('/')}/app/email-change/confirm#token={verifyRaw}";
        var revokeUrl = $"{revokeUrlBase.TrimEnd('/')}/app/email-change/revoke#token={revokeRaw}";

        try
        {
            await _email.SendAsync(BuildVerifyNewEmail(normalized, verifyUrl), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email-change verify email; token row already committed.");
        }

        try
        {
            await _email.SendAsync(BuildRevokeOldEmail(user.Email!, normalized, revokeUrl), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email-change revoke email; token row already committed.");
        }

        await _auditLog.RecordAsync(user.Id, AuditLogAction.EmailChangeRequested, ct: ct);

        return new EmailChangeRequestOutcome.Accepted();
    }

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
        var now = DateTime.UtcNow;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Cross-tenant by design: lookup by TokenLookup before the caller is authenticated. Stage 10 architecture test allow-lists this file.
        var match = await _db.EmailChangeTokens
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
            // Cross-tenant by design: re-read inside lock; still pre-auth. Stage 10 architecture test allow-lists this file.
            var current = await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= DateTime.UtcNow)
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
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            // Consume sibling RevokeOld row.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user.Id
                         && t.Purpose == EmailChangeTokenPurpose.RevokeOld
                         && t.NewEmail == match.NewEmail
                         && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            // Bulk-revoke all sessions for this user.
            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.UserSessions
                .IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);

            // SecurityStamp regen — invalidates any in-flight Identity cookies via SecurityStampValidator.
            await _userManager.UpdateSecurityStampAsync(user);

            // Notifications — never block the return.
            // New address (now address-of-record): "your email was just changed to this address".
            try
            {
                await _email.SendAsync(BuildChangeConfirmedEmail(match.NewEmail), ct);
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
                    await _email.SendAsync(BuildChangeConfirmedToOldEmail(oldEmail, match.NewEmail), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email-change confirmed notification (old address).");
                }
            }

            await _auditLog.RecordAsync(match.UserId, AuditLogAction.EmailChangeConfirmed, ct: ct);

            return new EmailChangeConfirmOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<EmailChangeRevokeOutcome> RevokeAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _argon.RunDummyHash();
            return new EmailChangeRevokeOutcome.InvalidToken();
        }

        // Stage 6.15: O(1) indexed lookup via HMAC-derived TokenLookup column.
        var now = DateTime.UtcNow;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // Cross-tenant by design: lookup by TokenLookup before the caller is authenticated. Stage 10 architecture test allow-lists this file.
        var match = await _db.EmailChangeTokens
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
            // Cross-tenant by design: re-read inside lock; still pre-auth. Stage 10 architecture test allow-lists this file.
            var current = await _db.EmailChangeTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= DateTime.UtcNow)
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
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            // Notify the OLD address only. The change is cancelled; user.Email is unchanged.
            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is not null)
            {
                try
                {
                    await _email.SendAsync(BuildRevokeNotificationToOldEmail(user.Email!), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email-change revoke notification.");
                }
            }

            await _auditLog.RecordAsync(match.UserId, AuditLogAction.EmailChangeRevoked, ct: ct);

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
        if (entry is null || entry.WindowStart + EmailRateWindow <= DateTime.UtcNow)
        {
            entry = new RateBucket { WindowStart = DateTime.UtcNow, Count = 1 };
            _cache.Set(key, entry, EmailRateWindow);
            return;
        }

        if (entry.Count >= EmailRateLimit)
        {
            var elapsed = DateTime.UtcNow - entry.WindowStart;
            var remaining = (int)Math.Ceiling((EmailRateWindow - elapsed).TotalSeconds);
            throw new RateLimitedException(Math.Max(remaining, 1));
        }

        entry.Count += 1;
        _cache.Set(key, entry, entry.WindowStart + EmailRateWindow - DateTime.UtcNow);
    }

    private sealed class RateBucket
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }

    private static EmailMessage BuildVerifyNewEmail(string newEmail, string verifyUrl)
    {
        const string subject = "Confirm your new Project Ceres email address";
        var bodyText = $"""
            We received a request to change the email address on your Project Ceres account
            to this address.

            Click or paste this link into your browser to confirm:
            {verifyUrl}

            This link expires in {(int)VerifyTokenLifetime.TotalMinutes} minutes and can only be used once.
            If you did not request this change, ignore this email.
            """;
        var bodyHtml = $"""
            <p>We received a request to change the email address on your Project Ceres account to this address.</p>
            <p><a href="{verifyUrl}">Confirm your new email address</a></p>
            <p>This link expires in {(int)VerifyTokenLifetime.TotalMinutes} minutes and can only be used once.</p>
            <p>If you did not request this change, ignore this email.</p>
            """;
        return new EmailMessage(newEmail, subject, bodyHtml, bodyText);
    }

    private static EmailMessage BuildRevokeOldEmail(string oldEmail, string newEmail, string revokeUrl)
    {
        const string subject = "An email change was requested on your Project Ceres account";
        var bodyText = $"""
            Someone (probably you) requested a change to your Project Ceres email address
            from {oldEmail} to {newEmail}.

            Until the new address is confirmed, this address remains your address of record.

            If this was NOT you, click or paste this link to cancel the change:
            {revokeUrl}

            This cancellation link is valid for {(int)RevokeTokenLifetime.TotalDays} days.
            """;
        var bodyHtml = $"""
            <p>Someone (probably you) requested a change to your Project Ceres email address from <strong>{oldEmail}</strong> to <strong>{newEmail}</strong>.</p>
            <p>Until the new address is confirmed, this address remains your address of record.</p>
            <p>If this was NOT you, <a href="{revokeUrl}">cancel the change</a>.</p>
            <p>This cancellation link is valid for {(int)RevokeTokenLifetime.TotalDays} days.</p>
            """;
        return new EmailMessage(oldEmail, subject, bodyHtml, bodyText);
    }

    private static EmailMessage BuildChangeConfirmedEmail(string newEmail)
    {
        const string subject = "Your Project Ceres email address was changed";
        const string bodyText = """
            Your Project Ceres email address was just changed to this address.

            All other active sessions have been signed out as a precaution.

            If this was NOT you, contact support immediately.
            """;
        const string bodyHtml = """
            <p>Your Project Ceres email address was just changed to this address.</p>
            <p>All other active sessions have been signed out as a precaution.</p>
            <p>If this was NOT you, contact support immediately.</p>
            """;
        return new EmailMessage(newEmail, subject, bodyHtml, bodyText);
    }

    private static EmailMessage BuildChangeConfirmedToOldEmail(string oldEmail, string newEmail)
    {
        const string subject = "Your Project Ceres email address was changed";
        var bodyText = $"""
            The Project Ceres account previously associated with this address ({oldEmail})
            has been moved to {newEmail}.

            All other active sessions have been signed out as a precaution.

            If this was NOT you, contact support immediately — you may still be able to
            recover the account.
            """;
        var bodyHtml = $"""
            <p>The Project Ceres account previously associated with this address (<strong>{oldEmail}</strong>) has been moved to <strong>{newEmail}</strong>.</p>
            <p>All other active sessions have been signed out as a precaution.</p>
            <p>If this was NOT you, contact support immediately — you may still be able to recover the account.</p>
            """;
        return new EmailMessage(oldEmail, subject, bodyHtml, bodyText);
    }

    private static EmailMessage BuildRevokeNotificationToOldEmail(string oldEmail)
    {
        const string subject = "Email change cancelled";
        const string bodyText = """
            The pending email change on your Project Ceres account has been cancelled.

            Your account email address is unchanged.

            If you did not initiate this cancellation, contact support immediately.
            """;
        const string bodyHtml = """
            <p>The pending email change on your Project Ceres account has been cancelled.</p>
            <p>Your account email address is unchanged.</p>
            <p>If you did not initiate this cancellation, contact support immediately.</p>
            """;
        return new EmailMessage(oldEmail, subject, bodyHtml, bodyText);
    }
}
