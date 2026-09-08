using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth")]
[PreAuthScope]
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
    private readonly LockoutCache _lockoutCache;
    private readonly EmailConfirmationService _emailConfirmation;
    private readonly TimeProvider _timeProvider;
    private readonly Common.Email.INewSessionNotificationService _newSessionNotifier;

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
        Services.CategorySeedService categorySeedService,
        LockoutCache lockoutCache,
        EmailConfirmationService emailConfirmation,
        TimeProvider timeProvider,
        Common.Email.INewSessionNotificationService newSessionNotifier)
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
        _lockoutCache = lockoutCache;
        _emailConfirmation = emailConfirmation;
        _timeProvider = timeProvider;
        _newSessionNotifier = newSessionNotifier;
    }

    private (string ip, string ua) RequestContext() =>
        (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
         Request.Headers.UserAgent.ToString());

    [HttpPost("register"), AllowAnonymous, PreAuthCallSite("Auth.Register")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Atomicity: AspNetUsers row + category seed + audit log entry must commit or
        // roll back together. Without the transaction, a post-CreateAsync failure
        // (DB blip, request cancellation, seeding bug) leaves an orphan AspNetUsers
        // row, and re-registration silently returns 204 per anti-enumeration —
        // permanently locking the email out.
        //
        // RLS: Categories carries a user_isolation policy that gates inserts on
        // app.current_user_ref = NEW."UserId". Pre-generate user.Id so the
        // PreAuthUserScope can set the GUC before CategorySeedService inserts the
        // default categories. EF preserves a non-default Guid set on the entity;
        // Identity.CreateAsync uses Add+SaveChanges and does not regenerate it.
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email };
        await using var tx = await _db.BeginPreAuthUserScopeAsync(user.Id, HttpContext.RequestAborted);

        var result = await _userManager.CreateAsync(user, request.Password);

        var verifyUrlBase = $"{Request.Scheme}://{Request.Host}";

        if (!result.Succeeded)
        {
            // Stage 9.3: three-branch anti-enumeration on duplicate-email paths.
            //  - Confirmed-existing user → dummy Argon2id to mirror IssueAsync cost.
            //  - Unconfirmed-existing user → issue a fresh token (same code path as fresh-create).
            //  - All other Create failures (short password, breached) → 400 via ValidationProblem(ModelState), which bypasses the 422 factory.
            var isDuplicateOnly = result.Errors.All(e =>
                e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail");
            if (isDuplicateOnly && result.Errors.Any())
            {
                // Capture existing-user state inside the outer tx, then commit, then issue.
                // IssueAsync opens its own PreAuthUserScope; calling it while the outer tx
                // is still open trips PreAuthRlsScope's nested-tx mismatch guard (the outer
                // empty tx has no app.current_user_ref GUC set).
                var existing = await _userManager.FindByEmailAsync(request.Email);
                var existingId = existing?.Id;
                var existingEmail = existing?.Email;
                var existingConfirmed = existing?.EmailConfirmed ?? false;

                await tx.CommitAsync(HttpContext.RequestAborted);

                if (existing is not null)
                {
                    if (existingConfirmed)
                    {
                        _argon.RunDummyHash();
                    }
                    else
                    {
                        await _emailConfirmation.IssueAsync(
                            existingId!.Value, existingEmail!, verifyUrlBase, HttpContext.RequestAborted);
                    }
                }
                return NoContent();
            }

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

        // Issue email-verification token AFTER the tx commits so a send failure
        // does not roll back user creation. Send failures are swallowed inside
        // IssueAsync per the PasswordResetService.RequestAsync pattern.
        await _emailConfirmation.IssueAsync(user.Id, user.Email!, verifyUrlBase, HttpContext.RequestAborted);

        return NoContent();
    }

    [HttpPost("login"), AllowAnonymous, PreAuthCallSite("Auth.Login")]
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
        // Fully qualified: both Identity and Mvc define SignInResult (CS0104). Do not shorten.
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

        if (signIn.IsNotAllowed)
        {
            // Stage 9.3: SignInManager flags IsNotAllowed when EmailConfirmed=false
            // (Identity's RequireConfirmedAccount). Surface a distinct code so the
            // SPA can render the "verify your email" CTA + resend link.
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(
                request.Email, userStub.Id, FailedLoginReason.EmailNotConfirmed, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("EMAIL_NOT_CONFIRMED",
                "Verify your email before signing in. Check your inbox or request a new link.");
        }

        if (signIn.IsLockedOut)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, userStub.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);

            // Stage 9.1.5.b: seed LockoutCache so OnRejected can surface ACCOUNT_LOCKED_OUT
            // on subsequent 429s. Memory-only writes; no additional DB read. Per-IP pointer
            // expires at IpPointerTtl (60s = rate-limit window) so other accounts on the
            // same NAT aren't mis-flagged for longer than the rate-limit cooldown itself.
            //
            // IP key uses "unknown" fallback to match the rate-limiter's partition key on
            // requests where Connection.RemoteIpAddress is null (e.g. TestServer hops).
            // Without this alignment, controller writes under "" and OnRejected reads under
            // "" — both no-op due to LockoutCache's empty-IP guard — so the cache never
            // wires across the two middleware hops.
            if (userStub.LockoutEnd is { } lockoutEnd)
            {
                var cacheIp = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
                _lockoutCache.SetLockoutEnd(request.Email, lockoutEnd);
                _lockoutCache.SetLastLockedEmailForIp(cacheIp, request.Email);
            }

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
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", AuthMessages.AccountTemporarilyLockedFifteenMinutes);
        }

        if (!signIn.Succeeded)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, userStub.Id, FailedLoginReason.BadCredentials, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        await IssueSessionAndCookiesAsync(userStub, sessionId, request.RememberMe, usedBackupCode: false);
        await _auditLog.RecordAsync(userStub.Id, AuditLogAction.LoginSucceeded, ct: HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("login/totp"), AllowAnonymous, PreAuthCallSite("Auth.LoginTotp")]
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
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", AuthMessages.AccountTemporarilyLockedFifteenMinutes);
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
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", AuthMessages.AccountTemporarilyLockedFifteenMinutes);
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
            // Stage 6b.2 replaced the framework's TwoFactorAuthenticatorSignInAsync (which
            // implicitly clears Identity.TwoFactorUserId) with manual VerifyTwoFactorToken +
            // SignInAsync to keep TOTP misses from poisoning the password lockout counter.
            // The trade-off: SignInAsync only sets the AuthenticationScheme cookie, so the
            // half-auth handoff cookie lingers until its 5-min TTL. Clear it explicitly so
            // the success path leaves no stale Identity cookies behind.
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe, usedBackupCode: false);
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
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", AuthMessages.AccountTemporarilyLockedFifteenMinutes);
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
            // See TOTP-success branch above — manual SignInAsync doesn't clear
            // the TwoFactorUserId scheme cookie; do it explicitly.
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe, usedBackupCode: true);
            ClearRememberMeCookie();
            await _auditLog.RecordAsync(user.Id, AuditLogAction.LoginSucceededBackupCode, ct: HttpContext.RequestAborted);
            return NoContent();
        }

        return UnauthorizedEnvelope("INVALID_MFA_CODE", "The verification code is invalid or expired.");
    }

    private async Task IssueSessionAndCookiesAsync(ApplicationUser user, Guid sessionId, bool rememberMe, bool usedBackupCode)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();

        // Stage 12.5.3: decide new-session-alert novelty BEFORE the dedup revoke below mutates
        // the row set. Exact-IP novelty (fork 1a, mirroring the anchor). First-ever login is
        // suppressed — with no prior session there is no "new" to alert on, and every first
        // sign-in would otherwise fire it. Alert only when the user HAS prior sessions and NONE
        // of them was created from this IP. Query is naturally owner-scoped (login runs in the
        // user's scope); IgnoreQueryFilters is not needed and not used.
        var priorSessions = await _db.UserSessions
            .Where(s => s.UserId == user.Id && s.Id != sessionId)
            .Select(s => s.IpCreatedAt)
            .ToListAsync();
        var isNovelSession = priorSessions.Count > 0 && !priorSessions.Contains(ip);

        // Dedup: one live session per device, persistent or not. Revoke any existing
        // live session from the same UA + IP before adding the new row, so the list
        // shows one row per device instead of one per login. Guarded by
        // Id != sessionId so we never revoke the row we are about to create; the new
        // cookie already carries the new sid, so SessionRevocationValidator still
        // accepts this request.
        //
        // Persistent rows must be included. Their rotation middleware only revokes the
        // row whose cookie the browser presents, and a browser holds exactly one
        // persistent cookie — so a superseded row is unreachable by rotation and would
        // otherwise keep a live PersistentTokenHash for the full 30-day lifetime.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.UserSessions
            .Where(s => s.UserId == user.Id
                && s.RevokedAt == null
                && s.UserAgent == ua
                && s.IpCreatedAt == ip
                && s.Id != sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));

        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            IpCreatedAt = ip,
            UserAgent = ua,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            LastUsedAt = _timeProvider.GetUtcNow().UtcDateTime,
            IsPersistent = rememberMe,
            UsedBackupCodeAtLogin = usedBackupCode,
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
                    Expires = DateTimeOffset.UtcNow.Add(SessionConstants.PersistentLifetime),
                });
        }

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync();

        _antiforgery.GetAndStoreTokens(HttpContext);

        // Stage 12.5.3: fire the new-session security alert after the session is committed.
        // Non-blocking — the service swallows + logs any send failure so a mail outage never
        // fails the login. No opt-out is consulted (security alert; prefs surface deferred).
        if (isNovelSession)
        {
            await _newSessionNotifier.NotifyNewSessionAsync(
                user.Id, ip, ua, session.CreatedAt, HttpContext.RequestAborted);
        }
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
    /// GET endpoint that refreshes the __Host-XSRF cookie and surfaces the CSRF
    /// request token for the SPA. State-changing endpoints require a CSRF cookie +
    /// matching X-XSRF-TOKEN header; this endpoint is the idiomatic way for the SPA
    /// (and integration tests) to ensure both are present and bound to the current
    /// authentication context. Side-effect-free, allowed to be GET.
    ///
    /// Response headers:
    ///   Set-Cookie: __Host-XSRF=&lt;cookie-token&gt; — set automatically by IAntiforgery.
    ///   X-XSRF-TOKEN: &lt;request-token&gt; — the SPA must echo this value as the
    ///     X-XSRF-TOKEN request header on all subsequent POST/PUT/PATCH/DELETE calls.
    ///
    /// ASP.NET's IAntiforgery validates a cryptographic pair (cookie token + request
    /// token). The two are distinct values; sending the cookie value as the header
    /// would be a mismatch and trigger a 400. The cookie token travels via Set-Cookie;
    /// the request token is emitted here so the SPA has an explicit exit channel.
    /// </summary>
    [HttpGet("csrf"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthCsrfByIp)]
    public IActionResult Csrf()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        // The cookie token goes to Set-Cookie automatically (configured in Program.cs).
        // The request token must be exposed to the SPA via a response header so apiFetch
        // can echo it on subsequent state-changing requests. ASP.NET's IAntiforgery uses
        // a cryptographic pair (cookie-token + request-token); sending the cookie value
        // as the header would be a mismatch (the pre-fix bug).
        Response.Headers[SessionConstants.CsrfHeaderName] = tokens.RequestToken;
        return NoContent();
    }

    /// <summary>
    /// Returns a snapshot of the current user's session state. Called once on mount
    /// by the SPA auth context to decide if the user is logged in and to render
    /// auth-aware UI (TOTP-enabled badges, the backup-code banner).
    /// Cache-Control: no-store — the SPA must always fetch fresh session state.
    /// </summary>
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");

        var lastReauthClaim = User.FindFirst(SessionConstants.LastReauthAtClaim)?.Value;
        long? lastReauthAt = long.TryParse(lastReauthClaim, out var v) ? v : null;

        var backupCodesRemaining = user.TwoFactorEnabled
            ? await _db.UserMfaBackupCodes.CountAsync(c => c.UserId == user.Id && c.UsedAt == null, HttpContext.RequestAborted)
            : 0;

        var sidClaim = User.FindFirst(SessionConstants.SessionIdClaim)?.Value;
        var usedBackupCodeAtLastLogin = false;
        if (Guid.TryParse(sidClaim, out var sessionIdForFlag))
        {
            usedBackupCodeAtLastLogin = await _db.UserSessions
                .Where(s => s.Id == sessionIdForFlag && s.UserId == user.Id && s.RevokedAt == null)
                .Select(s => s.UsedBackupCodeAtLogin)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
        }

        Response.ApplyNoStore();

        return Ok(new MeResponse(
            UserId: user.Id,
            Email: user.Email!,
            TwoFactorEnabled: user.TwoFactorEnabled,
            LastReauthAt: lastReauthAt,
            BackupCodesRemaining: backupCodesRemaining,
            UsedBackupCodeAtLastLogin: usedBackupCodeAtLastLogin));
    }

    [HttpPost("logout"), Authorize]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Logout()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userIdForAudit))
        {
            _logger.LogWarning("Logout called with unparseable NameIdentifier claim; audit row skipped.");
        }

        if (Guid.TryParse(User.FindFirstValue(SessionConstants.SessionIdClaim), out var sid))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = _timeProvider.GetUtcNow().UtcDateTime;
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
                    match.RevokedAt = _timeProvider.GetUtcNow().UtcDateTime;
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
