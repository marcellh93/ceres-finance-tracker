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
/// Orchestrates the password-reset flow per docs/superpowers/specs/2026-05-10-password-reset-design.md.
/// Per-user semaphore serialises the token supersede+insert. Per-email rate gate is service-side
/// (MemoryCache); per-IP gate is the existing AuthLoginByIp policy applied at the controller.
/// </summary>
public sealed class PasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly PasswordResetTokenGenerator _tokens;
    private readonly TotpReplayGuard _replayGuard;
    private readonly FailedLoginRecorder _failedLogins;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordResetService> _logger;
    private readonly IAuditLogWriter _auditLog;

    public PasswordResetService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PasswordResetTokenGenerator tokens,
        TotpReplayGuard replayGuard,
        FailedLoginRecorder failedLogins,
        IEmailService email,
        IMemoryCache cache,
        ILogger<PasswordResetService> logger,
        IAuditLogWriter auditLog)
    {
        _userManager = userManager;
        _signInManager = signInManager; // Reserved for ConfirmAsync (Task 13)
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _replayGuard = replayGuard;    // Reserved for ConfirmAsync (Task 13)
        _failedLogins = failedLogins;
        _email = email;
        _cache = cache;
        _logger = logger;
        _auditLog = auditLog;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Password reset rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    public async Task RequestAsync(
        string email, string ip, string userAgent, string resetUrlBase, CancellationToken ct)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            _argon.RunDummyHash();
            return;
        }

        // Per-email rate gate (5/hour). Holds counters in MemoryCache keyed by email.
        // Throws RateLimitedException; controller maps to 429.
        EnforceEmailRateLimit(normalized);

        var user = await _userManager.FindByEmailAsync(normalized);

        // Always pay the Argon2id cost — equalise wall-clock time across known/unknown branches.
        _argon.RunDummyHash();

        if (user is null)
        {
            await _failedLogins.RecordAsync(
                normalized, null, FailedLoginReason.PasswordResetUnknownEmail, ip, userAgent, ct);
            return;
        }

        var sem = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            // Supersede prior unused tokens.
            await _db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);

            var now = DateTime.UtcNow;
            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = hash,
                CreatedAt = now,
                ExpiresAt = now + TokenLifetime,
                ConsumedAt = null,
                MfaVerifiedAt = null,
            });
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var resetUrl = $"{resetUrlBase.TrimEnd('/')}/app/password-reset#token={rawToken}";

        try
        {
            await _email.SendAsync(BuildRequestEmail(user.Email!, resetUrl), ct);
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

    private static EmailMessage BuildRequestEmail(string to, string resetUrl)
    {
        const string subject = "Reset your Project Ceres password";
        var bodyText = $"""
            We received a request to reset your Project Ceres password.

            Click or paste this link into your browser to set a new password:
            {resetUrl}

            This link expires in {(int)TokenLifetime.TotalMinutes} minutes and can only be used once.
            If you did not request a reset, you can ignore this email.
            """;
        var bodyHtml = $"""
            <p>We received a request to reset your Project Ceres password.</p>
            <p><a href="{resetUrl}">Reset your password</a></p>
            <p>This link expires in {(int)TokenLifetime.TotalMinutes} minutes and can only be used once.</p>
            <p>If you did not request a reset, you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }

    public async Task<PasswordResetConfirmOutcome> ConfirmAsync(
        string rawToken, string newPassword, string? totpCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            // Constant-time: still pay one Argon2 verify against a dummy hash.
            _argon.RunDummyHash();
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        var now = DateTime.UtcNow;
        var candidates = await _db.PasswordResetTokens
            .Where(t => t.ConsumedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        PasswordResetToken? match = null;
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
            // Even with zero candidates, run one verify so timing doesn't reveal "no candidates".
            if (candidates.Count == 0) _argon.RunDummyHash();
            return new PasswordResetConfirmOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Re-read the token row inside the lock; another concurrent caller may have consumed it.
            var current = await _db.PasswordResetTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= DateTime.UtcNow)
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
            if (mfaVerified)
            {
                await _db.PasswordResetTokens
                    .Where(t => t.Id == match.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.ConsumedAt, DateTime.UtcNow)
                        .SetProperty(t => t.MfaVerifiedAt, DateTime.UtcNow), ct);
            }
            else
            {
                await _db.PasswordResetTokens
                    .Where(t => t.Id == match.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);
            }

            // Bulk-revoke all sessions for this user.
            await _db.UserSessions
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);

            // Cancel any pending email-change for this user. Reasoning: a password reset
            // is itself a recovery/compromise signal. If an attacker initiated /email-change/request
            // before being reset out, leaving the verify token live for up to 30 minutes would let
            // them complete the takeover after the legitimate user resets. Stage 6.12.
            var hadPendingEmailChange = await _db.EmailChangeTokens
                .AnyAsync(t => t.UserId == user.Id && t.ConsumedAt == null, ct);
            if (hadPendingEmailChange)
            {
                await _db.EmailChangeTokens
                    .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

                try
                {
                    await _email.SendAsync(BuildEmailChangeCancelledByPasswordResetEmail(user.Email!), ct);
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
                await _email.SendAsync(BuildChangedEmail(user.Email!), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password-changed notification email.");
            }

            await _auditLog.RecordAsync(user.Id, AuditLogAction.PasswordResetCompleted, ct: ct);

            return new PasswordResetConfirmOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

    private static EmailMessage BuildEmailChangeCancelledByPasswordResetEmail(string to)
    {
        const string subject = "Pending email change cancelled";
        const string bodyText = """
            Your Project Ceres password was just reset. Any pending change to your
            account email address has been cancelled as a precaution.

            Your account email address is unchanged.

            If you did not reset your password, contact support immediately.
            """;
        const string bodyHtml = """
            <p>Your Project Ceres password was just reset. Any pending change to your account email address has been cancelled as a precaution.</p>
            <p>Your account email address is unchanged.</p>
            <p>If you did not reset your password, contact support immediately.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }

    private static EmailMessage BuildChangedEmail(string to)
    {
        const string subject = "Your Project Ceres password was changed";
        const string bodyText = """
            Your Project Ceres password was just changed.

            If this was you, no further action is needed. All other active sessions
            have been signed out as a precaution.

            If you did not change your password, contact support immediately.
            """;
        var bodyHtml = """
            <p>Your Project Ceres password was just changed.</p>
            <p>If this was you, no further action is needed. All other active
            sessions have been signed out as a precaution.</p>
            <p>If you did not change your password, contact support immediately.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }
}
