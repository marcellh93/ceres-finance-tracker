using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        PersistentTokenService tokens,
        IAntiforgery antiforgery)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _argon = argon;
        _tokens = tokens;
        _antiforgery = antiforgery;
    }

    [HttpPost("register"), AllowAnonymous]
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
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Constant-time enumeration prevention: still pay the Argon2id cost.
            _argon.RunDummyHash();
            return Unauthorized();
        }

        var sessionId = Guid.NewGuid();
        HttpContext.Items[SessionConstants.PendingSessionItemKey] = sessionId;

        var signIn = await _signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (!signIn.Succeeded)
        {
            HttpContext.Items.Remove(SessionConstants.PendingSessionItemKey);
            return Unauthorized();
        }

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
            IsPersistent = request.RememberMe,
        };

        if (request.RememberMe)
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

        // Rotate CSRF cookie on login.
        _antiforgery.GetAndStoreTokens(HttpContext);

        return NoContent();
    }

    /// <summary>
    /// GET endpoint that refreshes the __Host-XSRF cookie. State-changing endpoints
    /// require a CSRF cookie + matching X-XSRF-TOKEN header; this endpoint is the
    /// idiomatic way for the SPA (and integration tests) to ensure both are present
    /// and bound to the current authentication context. Side-effect-free, allowed
    /// to be GET.
    /// </summary>
    [HttpGet("csrf"), AllowAnonymous]
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
