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
/// Per-user semaphore mirrors AuthController._loginLocks. Per-email rate gate is service-side
/// (MemoryCache); per-IP gate is the existing AuthLoginByIp policy applied at the controller.
/// </summary>
public sealed class PasswordResetService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _emailLocks = new();

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
        ILogger<PasswordResetService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _replayGuard = replayGuard;
        _failedLogins = failedLogins;
        _email = email;
        _cache = cache;
        _logger = logger;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds) =>
            RetryAfterSeconds = retryAfterSeconds;
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
    }

    private void EnforceEmailRateLimit(string normalizedEmail)
    {
        // Sliding-window-ish: if the cached counter for this email reaches the limit,
        // reject. Cache TTL = the window length, so the bucket auto-resets.
        var sem = _emailLocks.GetOrAdd(normalizedEmail, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        try
        {
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
        finally
        {
            sem.Release();
        }
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

            This link expires in 15 minutes and can only be used once.
            If you did not request a reset, you can ignore this email.
            """;
        var bodyHtml = $"""
            <p>We received a request to reset your Project Ceres password.</p>
            <p><a href="{resetUrl}">Reset your password</a></p>
            <p>This link expires in 15 minutes and can only be used once.</p>
            <p>If you did not request a reset, you can ignore this email.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }
}
