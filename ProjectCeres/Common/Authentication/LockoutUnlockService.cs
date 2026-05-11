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
public sealed class LockoutUnlockService
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _userLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly LockoutUnlockTokenGenerator _tokens;
    private readonly IEmailService _email;
    private readonly ILogger<LockoutUnlockService> _logger;
    private readonly IAuditLogWriter _auditLog;

    public LockoutUnlockService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        LockoutUnlockTokenGenerator tokens,
        IEmailService email,
        ILogger<LockoutUnlockService> logger,
        IAuditLogWriter auditLog)
    {
        _userManager = userManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _email = email;
        _logger = logger;
        _auditLog = auditLog;
    }

    public async Task IssueAsync(
        Guid userId, string userEmail, string ip, string userAgent,
        string unlockUrlBase, CancellationToken ct)
    {
        var sem = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        string rawToken;
        try
        {
            await _db.LockoutUnlockTokens
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
            await _email.SendAsync(BuildLockoutEmail(userEmail, unlockUrl, ip), ct);
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
        var candidates = await _db.LockoutUnlockTokens
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
            var current = await _db.LockoutUnlockTokens
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

            await _db.LockoutUnlockTokens
                .Where(t => t.Id == match.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, DateTime.UtcNow), ct);

            await _auditLog.RecordAsync(user.Id, AuditLogAction.LockoutSelfServiceUnlock, ct: ct);

            return new LockoutUnlockOutcome.Success();
        }
        finally
        {
            sem.Release();
        }
    }

    private static EmailMessage BuildLockoutEmail(string to, string unlockUrl, string ip)
    {
        const string subject = "Your Project Ceres account was locked";
        var bodyText = $"""
            Your Project Ceres account was just locked after several failed sign-in
            attempts from IP address {ip}.

            If this was you, your account will automatically unlock in 15 minutes.
            You can also unlock it now by clicking or pasting this link:
            {unlockUrl}

            This link expires in 15 minutes and can only be used once.

            If you did not attempt to sign in, your password may have been guessed.
            We recommend resetting your password from the sign-in page.
            """;
        var bodyHtml = $"""
            <p>Your Project Ceres account was just locked after several failed
            sign-in attempts from IP address <code>{ip}</code>.</p>
            <p>If this was you, your account will automatically unlock in 15 minutes.
            You can also unlock it now: <a href="{unlockUrl}">Unlock account</a>.</p>
            <p>This link expires in 15 minutes and can only be used once.</p>
            <p>If you did not attempt to sign in, your password may have been guessed.
            We recommend resetting your password from the sign-in page.</p>
            """;
        return new EmailMessage(to, subject, bodyHtml, bodyText);
    }
}
