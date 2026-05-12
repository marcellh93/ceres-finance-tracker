using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    // Per-user semaphore: serializes concurrent PasswordSignInAsync calls for the same
    // user within this process. Without serialization, concurrent bad-password requests
    // race on the Identity ConcurrencyStamp: all load the user simultaneously, all try
    // to UPDATE AspNetUsers WHERE ConcurrencyStamp = @old, only one wins, and the
    // AccessFailedCount barely increments. This prevents lockout from engaging reliably.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim>
        _loginLocks = new();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly PersistentTokenService _tokens;
    private readonly IAntiforgery _antiforgery;
    private readonly FailedLoginRecorder _failedLogins;
    private readonly IAuditLogWriter _auditLog;
    private readonly LockoutUnlockService _lockoutUnlock;
    private readonly ILogger<AuthController> _logger;
    private readonly Services.CategorySeedService _categorySeedService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PersistentTokenService tokens,
        IAntiforgery antiforgery,
        FailedLoginRecorder failedLogins,
        IAuditLogWriter auditLog,
        LockoutUnlockService lockoutUnlock,
        ILogger<AuthController> logger,
        Services.CategorySeedService categorySeedService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _antiforgery = antiforgery;
        _failedLogins = failedLogins;
        _auditLog = auditLog;
        _lockoutUnlock = lockoutUnlock;
        _logger = logger;
        _categorySeedService = categorySeedService;
    }

    private (string ip, string ua) RequestContext() =>
        (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
         Request.Headers.UserAgent.ToString());

    [HttpPost("register"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Atomicity: AspNetUsers row + category seed + audit log entry must commit or
        // roll back together. Without the transaction, a post-CreateAsync failure
        // (DB blip, request cancellation, seeding bug) leaves an orphan AspNetUsers
        // row, and re-registration silently returns 204 per anti-enumeration —
        // permanently locking the email out.
        await using var tx = await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted);

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Stage 6b.3 Gap 6: don't leak account existence. If the failure is purely the
            // duplicate-username case, return 204 same as a fresh registration. All other
            // failure classes (short password, breached, malformed) still return 422.
            // Stage 6c follow-up: send "someone tried to register with your email" notice
            // on the duplicate path once email-send ships.
            var isDuplicateOnly = result.Errors.All(e =>
                e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail");
            if (isDuplicateOnly && result.Errors.Any())
                return NoContent();

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }
            return ValidationProblem(ModelState);
        }
        // Stage 7: every user owns their own copy of the default categories.
        await _categorySeedService.CopyDefaultsForUserAsync(user.Id, HttpContext.RequestAborted);
        await _auditLog.RecordAsync(user.Id, AuditLogAction.Registered, ct: HttpContext.RequestAborted);
        await tx.CommitAsync(HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("login"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // First pass: resolve the user record to get the ID for the per-user lock key.
        // We intentionally do NOT call PasswordSignInAsync here — Identity would load
        // the user a second time inside AccessFailedAsync anyway, and we need to pass
        // a freshly-loaded user object AFTER acquiring the lock (see comment below).
        var userStub = await _userManager.FindByEmailAsync(request.Email);
        if (userStub is null)
        {
            // Constant-time enumeration prevention: still pay the Argon2id cost.
            _argon.RunDummyHash();
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, null, FailedLoginReason.UnknownUser, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        var sessionId = Guid.NewGuid();
        HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;
        HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Serialize concurrent login attempts for the same user with an in-process
        // per-user semaphore. Without serialization, concurrent bad-password requests
        // race on the Identity ConcurrencyStamp in AspNetUsers: all read the user
        // simultaneously, all try to UPDATE WHERE ConcurrencyStamp = @old, only one
        // UPDATE wins, and AccessFailedCount barely increments — lockout never engages.
        //
        // We also reload the user inside the semaphore (via EF ReloadAsync) so
        // PasswordSignInAsync / AccessFailedAsync sees the current ConcurrencyStamp.
        // The userStub loaded above may be stale: a prior request may have incremented
        // AccessFailedCount between our FindByEmail and our semaphore acquisition.
        var loginSem = _loginLocks.GetOrAdd(userStub.Id, _ => new SemaphoreSlim(1, 1));
        await loginSem.WaitAsync(HttpContext.RequestAborted);
        Microsoft.AspNetCore.Identity.SignInResult signIn;
        bool lockoutTransitioned;
        try
        {
            await _db.Entry(userStub).ReloadAsync();
            var wasLockedBefore = userStub.LockoutEnd is not null && userStub.LockoutEnd > DateTimeOffset.UtcNow;
            signIn = await _signInManager.PasswordSignInAsync(
                userStub, request.Password, isPersistent: false, lockoutOnFailure: true);
            // Re-read post-call to pick up LockoutEnd flipped by AccessFailedAsync.
            await _db.Entry(userStub).ReloadAsync();
            var isLockedAfter = userStub.LockoutEnd is not null && userStub.LockoutEnd > DateTimeOffset.UtcNow;
            lockoutTransitioned = !wasLockedBefore && isLockedAfter;
        }
        finally
        {
            loginSem.Release();
        }

        if (signIn.RequiresTwoFactor)
        {
            // Identity has set Identity.TwoFactorUserId scoped cookie automatically.
            // No __Host-Session, no UserSession row — those wait for /login/totp.
            // Stash rememberMe so the TOTP step can honour it (HttpContext is per-request,
            // we cache it via a short-lived data-protected cookie).
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            _antiforgery.GetAndStoreTokens(HttpContext);
            // Forward rememberMe via a small cookie scoped to /api/auth/login/totp
            Response.Cookies.Append(
                "Mfa.RememberMe",
                request.RememberMe.ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/api/auth/login",
                    Expires = DateTimeOffset.UtcNow.AddMinutes(10),
                });
            return Ok(new { requiresTotp = true });
        }

        if (signIn.IsLockedOut)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, userStub.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
            if (lockoutTransitioned)
            {
                try
                {
                    var unlockUrlBase = $"{Request.Scheme}://{Request.Host}";
                    await _lockoutUnlock.IssueAsync(
                        userStub.Id, userStub.Email!, ip, ua, unlockUrlBase, HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    // IssueAsync internally swallows email failures; this catch covers an
                    // unexpected DB-write failure so it doesn't mask the user-visible lockout
                    // response.
                    _logger.LogError(ex, "Failed to issue lockout-unlock token for user {UserId}", userStub.Id);
                }
            }
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
        }

        if (!signIn.Succeeded)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, userStub.Id, FailedLoginReason.BadCredentials, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        await IssueSessionAndCookiesAsync(userStub, sessionId, request.RememberMe);
        await _auditLog.RecordAsync(userStub.Id, AuditLogAction.LoginSucceeded, ct: HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("login/totp"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthTotpByUser)]
    public async Task<IActionResult> LoginTotp(
        [FromBody] LoginTotpRequest request,
        [FromServices] TotpReplayGuard replayGuard,
        [FromServices] MfaBackupCodeService backupCodes)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Identity reads Identity.TwoFactorUserId from request cookies
        // and resolves the half-authenticated user.
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");

        var rememberMe = ReadRememberMeCookie();
        var sessionId = Guid.NewGuid();
        HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;

        if (MfaConstants.TotpCodeShape.IsMatch(request.Code))
        {
            // Stage 6b.2: replaced TwoFactorAuthenticatorSignInAsync (which silently
            // calls AccessFailedAsync on miss) with VerifyTwoFactorTokenAsync +
            // manual SignInAsync. TOTP misses no longer poison the password
            // lockout counter; brute-force defense is the per-user 10/min limiter
            // plus 30-second TOTP rotation.
            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
            if (!ok)
            {
                var (ip, ua) = RequestContext();
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, HttpContext.RequestAborted);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
                    await _signInManager.SignOutAsync();
                    ClearRememberMeCookie();
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                }
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            var accepted = await replayGuard.TryAcceptAsync(user.Id, request.Code, HttpContext.RequestAborted);
            if (!accepted)
            {
                var (ip, ua) = RequestContext();
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadTotp, ip, ua, HttpContext.RequestAborted);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
                    await _signInManager.SignOutAsync();
                    ClearRememberMeCookie();
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                }
                await _signInManager.SignOutAsync();
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            // If the account was locked, the TOTP success clears the lock.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
            await _auditLog.RecordAsync(user.Id, AuditLogAction.LoginSucceededMfa, ct: HttpContext.RequestAborted);
            return NoContent();
        }

        var stripped = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (MfaConstants.BackupCodeShape.IsMatch(stripped))
        {
            var (ip, ua) = RequestContext();
            var ok = await backupCodes.VerifyAndConsumeAsync(user.Id, request.Code, ip, HttpContext.RequestAborted);
            if (!ok)
            {
                await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.BadBackupCode, ip, ua, HttpContext.RequestAborted);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    await _failedLogins.RecordAsync(user.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
                    await _signInManager.SignOutAsync();
                    ClearRememberMeCookie();
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                }
                return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
            }

            // Backup-code success during lockout clears the lock — same policy as TOTP.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
            await _auditLog.RecordAsync(user.Id, AuditLogAction.LoginSucceededBackupCode, ct: HttpContext.RequestAborted);
            return NoContent();
        }

        return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
    }

    private async Task IssueSessionAndCookiesAsync(ApplicationUser user, Guid sessionId, bool rememberMe)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();

        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            IpCreatedAt = ip,
            UserAgent = ua,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsPersistent = rememberMe,
        };

        if (rememberMe)
        {
            // Issue cookie in {base64url(sessionIdBytes)}.{secret} format (Gap 3).
            // Only the secret is hashed; session ID is indexed in DB for O(1) lookup.
            var secret = _tokens.Generate();
            session.PersistentTokenHash = _tokens.Hash(secret);
            var cookieValue = _tokens.FormatCookie(sessionId, secret);
            Response.Cookies.Append(
                SessionConstants.PersistentCookieName,
                cookieValue,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                });
        }

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();

        _antiforgery.GetAndStoreTokens(HttpContext);
    }

    private bool ReadRememberMeCookie() =>
        Request.Cookies.TryGetValue("Mfa.RememberMe", out var raw) && bool.TryParse(raw, out var b) && b;

    private void ClearRememberMeCookie()
    {
        Response.Cookies.Delete("Mfa.RememberMe", new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth/login",
        });
    }

    /// <summary>
    /// Returns 401 with the standard api-contract.md error envelope:
    /// { error: { code, message } }. Use this for every Unauthorized return
    /// from auth-flow methods so the SPA gets a consistent shape.
    /// </summary>
    private IActionResult UnauthorizedEnvelope(string code, string message)
        => Unauthorized(new { error = new { code, message } });

    /// <summary>
    /// GET endpoint that refreshes the __Host-XSRF cookie. State-changing endpoints
    /// require a CSRF cookie + matching X-XSRF-TOKEN header; this endpoint is the
    /// idiomatic way for the SPA (and integration tests) to ensure both are present
    /// and bound to the current authentication context. Side-effect-free, allowed
    /// to be GET.
    /// </summary>
    [HttpGet("csrf"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthCsrfByIp)]
    public IActionResult Csrf()
    {
        _antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }

    [HttpPost("logout"), Authorize]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Logout()
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userIdForAudit);

        if (Guid.TryParse(User.FindFirstValue(SessionConstants.SessionIdClaim), out var sid))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        if (Request.Cookies.TryGetValue(SessionConstants.PersistentCookieName, out var rawCookie)
            && !string.IsNullOrWhiteSpace(rawCookie))
        {
            // Parse cookie to get session ID for O(1) indexed lookup (Gap 3).
            // Legacy raw-secret cookies (pre-6b.3) won't parse and are skipped.
            var parsed = _tokens.TryParseCookie(rawCookie);
            if (parsed is { } p)
            {
                var match = await _db.UserSessions
                    .Where(s => s.Id == p.sessionId && s.IsPersistent && s.RevokedAt == null && s.PersistentTokenHash != null)
                    .FirstOrDefaultAsync();

                if (match is not null && _tokens.Verify(p.secret, match.PersistentTokenHash!))
                {
                    match.RevokedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }
            Response.Cookies.Delete(SessionConstants.PersistentCookieName, new CookieOptions
            {
                Path = "/",
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
            });
        }

        await _signInManager.SignOutAsync();
        _antiforgery.GetAndStoreTokens(HttpContext);
        if (userIdForAudit != Guid.Empty)
            await _auditLog.RecordAsync(userIdForAudit, AuditLogAction.Logout, ct: HttpContext.RequestAborted);
        return NoContent();
    }
}
