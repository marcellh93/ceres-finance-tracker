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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AppDbContext _db;
    private readonly Argon2idPasswordHasher _argon;
    private readonly PersistentTokenService _tokens;
    private readonly IAntiforgery _antiforgery;
    private readonly FailedLoginRecorder _failedLogins;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PersistentTokenService tokens,
        IAntiforgery antiforgery,
        FailedLoginRecorder failedLogins)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _antiforgery = antiforgery;
        _failedLogins = failedLogins;
    }

    private (string ip, string ua) RequestContext() =>
        (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
         Request.Headers.UserAgent.ToString());

    [HttpPost("register"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }
            return ValidationProblem(ModelState);
        }
        return NoContent();
    }

    [HttpPost("login"), AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Constant-time enumeration prevention: still pay the Argon2id cost.
            _argon.RunDummyHash();
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, null, FailedLoginReason.UnknownUser, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        var sessionId = Guid.NewGuid();
        HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;

        var signIn = await _signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: false, lockoutOnFailure: true);

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
            await _failedLogins.RecordAsync(request.Email, user.Id, FailedLoginReason.LockedOut, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
        }

        if (!signIn.Succeeded)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            var (ip, ua) = RequestContext();
            await _failedLogins.RecordAsync(request.Email, user.Id, FailedLoginReason.BadCredentials, ip, ua, HttpContext.RequestAborted);
            return UnauthorizedEnvelope("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        await IssueSessionAndCookiesAsync(user, sessionId, request.RememberMe);
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
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");
            }

            var accepted = await replayGuard.TryAcceptAsync(user.Id, request.Code, HttpContext.RequestAborted);
            if (!accepted)
            {
                await _signInManager.SignOutAsync();
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");
            }

            // If the account was locked, the TOTP success clears the lock.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
            return NoContent();
        }

        var stripped = request.Code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (MfaConstants.BackupCodeShape.IsMatch(stripped))
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ok = await backupCodes.VerifyAndConsumeAsync(user.Id, request.Code, ip, HttpContext.RequestAborted);
            if (!ok)
                return UnauthorizedEnvelope("INVALID_MFA_CODE",
                    "The verification code is invalid or expired.");

            // Backup-code success during lockout clears the lock — same policy as TOTP.
            if (user.LockoutEnd.HasValue)
            {
                await _userManager.ResetAccessFailedCountAsync(user);
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            await IssueSessionAndCookiesAsync(user, sessionId, rememberMe);
            ClearRememberMeCookie();
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
            var rawToken = _tokens.Generate();
            session.PersistentTokenHash = _tokens.Hash(rawToken);
            Response.Cookies.Append(
                SessionConstants.PersistentCookieName,
                rawToken,
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
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public IActionResult Csrf()
    {
        _antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        if (Guid.TryParse(User.FindFirstValue(SessionConstants.SessionIdClaim), out var sid))
        {
            var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sid);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        if (Request.Cookies.TryGetValue(SessionConstants.PersistentCookieName, out var rawToken)
            && !string.IsNullOrWhiteSpace(rawToken))
        {
            var candidates = await _db.UserSessions
                .Where(s => s.IsPersistent && s.RevokedAt == null && s.PersistentTokenHash != null)
                .ToListAsync();
            var match = candidates.FirstOrDefault(c => _tokens.Verify(rawToken, c.PersistentTokenHash!));
            if (match is not null)
            {
                match.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            Response.Cookies.Delete(SessionConstants.PersistentCookieName);
        }

        await _signInManager.SignOutAsync();
        _antiforgery.GetAndStoreTokens(HttpContext);
        return NoContent();
    }
}
