using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _scopeFactory;

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
        IServiceScopeFactory scopeFactory)
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
        _scopeFactory = scopeFactory;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Password reset rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>
    /// Thrown when the requesting IP is on the user's blocked-IP list.
    /// Controller maps this to 403.
    /// </summary>
    public sealed class BlockedIpException : Exception
    {
        public BlockedIpException() : base("Request IP is blocked for this user.") { }
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

        // IP-block check: if the user exists and the requesting IP is on their blocked list,
        // reject with BlockedIpException (controller maps to 403). This mirrors what
        // UserBlockedIpMiddleware does for authenticated requests — applying it here
        // extends the protection to the anonymous /request endpoint.
        if (user is not null)
        {
            var isBlocked = await _db.UserBlockedIps
                .AnyAsync(b => b.UserId == user.Id && b.IpAddress == ip, ct);
            if (isBlocked)
            {
                throw new BlockedIpException();
            }
        }

        // Constant-time design: start the I/O work on a fresh scope in parallel with the
        // Argon2id hash so both known and unknown branches have approximately equal wall-clock
        // time (the hash dominates). The I/O task uses its own scope to avoid DbContext
        // concurrency issues; the request scope is not touched after launching the task.
        Task ioTask;
        if (user is null)
        {
            // Unknown email: fire FailedLogin recording in parallel with hash.
            var capturedNorm = normalized;
            var capturedIp = ip;
            var capturedUa = userAgent;
            ioTask = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();
                try
                {
                    await recorder.RecordAsync(
                        capturedNorm, null, FailedLoginReason.PasswordResetUnknownEmail,
                        capturedIp, capturedUa, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record failed-login attempt for password-reset unknown-email path.");
                }
            });
        }
        else
        {
            // Known email: fire token creation + email send in parallel with hash.
            var capturedUser = user;
            var capturedBase = resetUrlBase;
            ioTask = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tokens = scope.ServiceProvider.GetRequiredService<PasswordResetTokenGenerator>();
                var emailSvc = scope.ServiceProvider.GetRequiredService<IEmailService>();

                var sem = _userLocks.GetOrAdd(capturedUser.Id, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync(CancellationToken.None);
                string rawToken;
                try
                {
                    // Supersede prior unused tokens.
                    await db.PasswordResetTokens
                        .Where(t => t.UserId == capturedUser.Id && t.ConsumedAt == null)
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow),
                            CancellationToken.None);

                    rawToken = tokens.Generate();
                    var hash = tokens.Hash(rawToken);

                    var now = DateTime.UtcNow;
                    db.PasswordResetTokens.Add(new PasswordResetToken
                    {
                        Id = Guid.NewGuid(),
                        UserId = capturedUser.Id,
                        TokenHash = hash,
                        CreatedAt = now,
                        ExpiresAt = now + TokenLifetime,
                        ConsumedAt = null,
                        MfaVerifiedAt = null,
                    });
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                finally
                {
                    sem.Release();
                }

                var resetUrl = $"{capturedBase.TrimEnd('/')}/app/password-reset#token={rawToken}";
                try
                {
                    await emailSvc.SendAsync(BuildRequestEmail(capturedUser.Email!, resetUrl), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send password-reset email; token row already committed.");
                }
            });
        }

        // Always pay the Argon2id cost — equalise wall-clock time across known/unknown branches.
        // Running it after launching the ioTask means both branches overlap I/O with CPU work.
        _argon.RunDummyHash();

        // Await the I/O task so the service call doesn't return before the work completes
        // (ensures test assertions on the DB state are always valid).
        await ioTask;
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
}
