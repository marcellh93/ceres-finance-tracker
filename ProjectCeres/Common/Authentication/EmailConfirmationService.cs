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
/// Orchestrates registration email confirmation per Stage 9.3. Mirrors
/// PasswordResetService minus the MFA / password-policy / supersede-on-confirm
/// branches. Per-user semaphore serialises supersede+insert; per-email rate
/// gate is MemoryCache-backed (5/hour); per-IP gate is controller-side.
/// </summary>
[PreAuthScope]
[RequiresAdminContext]
public sealed class EmailConfirmationService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan EmailRateWindow = TimeSpan.FromHours(1);
    public const int EmailRateLimit = 5;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    // ConfirmAsync looks up the token row by TokenLookup before the userId is
    // known; the user_isolation RLS policy on ceres_app would filter every row
    // when app.current_user_ref is unset. Read via AdminDbContext (BYPASSRLS)
    // for the initial lookup, then open a PreAuthUserScope on _db once we
    // have match.UserId. Same pattern as PasswordResetService.ConfirmAsync.
    private readonly AdminDbContext _admin;
    private readonly Argon2idPasswordHasher _argon;
    private readonly EmailConfirmationTokenGenerator _tokens;
    private readonly TokenLookupHasher _lookupHasher;
    private readonly IEmailService _email;
    private readonly IEmailComposer _composer;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EmailConfirmationService> _logger;
    private readonly IAuditLogWriter _auditLog;
    private readonly ILookupNormalizer _normalizer;
    private readonly TimeProvider _timeProvider;

    public EmailConfirmationService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        AdminDbContext admin,
        Argon2idPasswordHasher argon,
        EmailConfirmationTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        IEmailService email,
        IEmailComposer composer,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        IMemoryCache cache,
        ILogger<EmailConfirmationService> logger,
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
        _logger = logger;
        _auditLog = auditLog;
        _normalizer = normalizer;
        _timeProvider = timeProvider;
    }

    public sealed class RateLimitedException : Exception
    {
        public int RetryAfterSeconds { get; }
        public RateLimitedException(int retryAfterSeconds)
            : base("Email-verification rate limit exceeded.")
            => RetryAfterSeconds = retryAfterSeconds;
    }

    [RlsBypassJustified("CER-1001")]
    public async Task IssueAsync(
        Guid userId, string email, string verifyUrlBase, CancellationToken ct)
    {
        var sem = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            // PreAuthUserScope so the user_isolation RLS policy passes on the
            // supersede UPDATE + INSERT; caller has no session cookie yet.
            await using var scope = await _db.BeginPreAuthUserScopeAsync(userId, ct);

            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId && t.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            rawToken = _tokens.Generate();
            var hash = _tokens.Hash(rawToken);
            var lookup = _lookupHasher.ComputeLookup(rawToken);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _db.EmailConfirmationTokens.Add(new EmailConfirmationToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
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

        var verifyUrl = $"{verifyUrlBase.TrimEnd('/')}/email-verify#token={rawToken}";

        try
        {
            var recipient = await _recipients.ResolveAsync(userId, ct);
            var culture = await _languages.ResolveForUserAsync(userId, ct);
            var msg = _composer.Compose(EmailTemplateKey.RegistrationConfirmation, culture, verifyUrl)
                with { To = recipient };
            await _email.SendAsync(msg, ct);
        }
        catch (Exception ex)
        {
            // Per spec: failed sends are logged but never block the user-facing request.
            _logger.LogError(ex, "Failed to send registration-confirmation email; token row already committed.");
        }

        await _auditLog.RecordAsync(userId, AuditLogAction.EmailVerificationRequested, ct: ct);
    }

    public async Task RequestResendAsync(string email, string verifyUrlBase, CancellationToken ct)
    {
        var trimmed = email?.Trim();
        var normalized = _normalizer.NormalizeEmail(trimmed) ?? trimmed ?? "";
        if (normalized.Length == 0)
        {
            _argon.RunDummyHash();
            return;
        }

        EnforceEmailRateLimit(normalized);

        // Equalise dominant Argon2id cost across all branches so an attacker
        // cannot distinguish unknown / confirmed / unconfirmed by timing.
        // The unconfirmed branch pays TWO Argon2id hashes (this one + the
        // Hash() inside IssueAsync). Unknown and confirmed branches mirror
        // both costs below.
        _argon.RunDummyHash();

        var user = await _userManager.FindByEmailAsync(normalized);
        if (user is null)
        {
            // Mirror the second Argon2id hash that the unconfirmed branch
            // pays via IssueAsync's _tokens.Hash(rawToken).
            _argon.RunDummyHash();
            return;
        }

        if (user.EmailConfirmed)
        {
            // Same mirror as the unknown branch — no token issued, but the
            // wall-clock cost matches the unconfirmed branch.
            _argon.RunDummyHash();
            return;
        }

        await IssueAsync(user.Id, user.Email!, verifyUrlBase, ct);
    }

    private void EnforceEmailRateLimit(string normalizedEmail)
    {
        // Sliding-window-ish: cached counter per email, TTL = window length.
        // No per-email semaphore — MemoryCache is thread-safe and the per-IP
        // limiter is the hard defence against abuse.
        var key = $"emailconfirm:rate:{normalizedEmail}";
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

    [RlsBypassJustified("CER-1002")]
    public async Task<EmailConfirmationConfirmOutcome> ConfirmAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            _argon.RunDummyHash();
            return new EmailConfirmationConfirmOutcome.InvalidToken();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lookup = _lookupHasher.ComputeLookup(rawToken);
        // AdminDbContext (BYPASSRLS) for the initial pre-userId lookup. IgnoreQueryFilters
        // bypasses the EF-level per-user global filter that AdminDbContext inherits.
        var match = await _admin.EmailConfirmationTokens
            .IgnoreQueryFilters()
            .Where(t => t.TokenLookup == lookup
                     && t.ConsumedAt == null
                     && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (match is null)
        {
            _argon.RunDummyHash();
            return new EmailConfirmationConfirmOutcome.InvalidToken();
        }

        if (!_tokens.Verify(rawToken, match.TokenHash))
        {
            return new EmailConfirmationConfirmOutcome.InvalidToken();
        }

        var sem = _userLocks.GetOrAdd(match.UserId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            // Now that match.UserId is known, open a PreAuthUserScope on _db
            // so the user_isolation policy passes on the subsequent writes.
            await using var rlsScope = await _db.BeginPreAuthUserScopeAsync(match.UserId, ct);

            // Re-read by Id inside the lock; another concurrent caller may have consumed it.
            var current = await _admin.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == match.Id, ct);
            if (current is null || current.ConsumedAt != null || current.ExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
            {
                return new EmailConfirmationConfirmOutcome.InvalidToken();
            }

            var user = await _userManager.FindByIdAsync(match.UserId.ToString());
            if (user is null)
            {
                return new EmailConfirmationConfirmOutcome.InvalidToken();
            }

            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);

            // Cross-tenant by design: token-based pre-auth operation. Stage 10 allow-lists this file.
            await _db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, _timeProvider.GetUtcNow().UtcDateTime), ct);

            await _auditLog.RecordAsync(user.Id, AuditLogAction.EmailVerified, ct: ct);

            await rlsScope.CommitAsync(ct);

            return new EmailConfirmationConfirmOutcome.Success(user.Id);
        }
        finally
        {
            sem.Release();
        }
    }
}
