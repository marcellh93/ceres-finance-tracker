using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Handles the __Host-Persist remember-me cookie. On request: if a __Host-Persist
/// cookie is present, look up matching UserSession by hashed token, rotate the
/// token (issue new + replace hash + clear old cookie), insert a fresh
/// UserSession row, sign user into the regular Identity scheme. Returns
/// NoResult if no cookie is present or no match is found.
/// </summary>
public sealed class PersistentCookieHandler : AuthenticationHandler<PersistentCookieOptions>
{
    private readonly AppDbContext _db;
    private readonly PersistentTokenService _tokens;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public PersistentCookieHandler(
        IOptionsMonitor<PersistentCookieOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        AppDbContext db,
        PersistentTokenService tokens,
        SignInManager<ApplicationUser> signInManager)
        : base(options, loggerFactory, encoder)
    {
        _db = db;
        _tokens = tokens;
        _signInManager = signInManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionConstants.PersistentCookieName, out var rawToken)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            return AuthenticateResult.NoResult();
        }

        var candidates = await _db.UserSessions
            .Where(s => s.IsPersistent && s.RevokedAt == null && s.PersistentTokenHash != null)
            .ToListAsync();

        var match = candidates.FirstOrDefault(c => _tokens.Verify(rawToken, c.PersistentTokenHash!));
        if (match is null)
        {
            return AuthenticateResult.NoResult();
        }

        match.RevokedAt = DateTime.UtcNow;

        var newSessionId = Guid.NewGuid();
        var newToken = _tokens.Generate();
        var newSession = new UserSession
        {
            Id = newSessionId,
            UserId = match.UserId,
            PersistentTokenHash = _tokens.Hash(newToken),
            IpCreatedAt = Context.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = Request.Headers.UserAgent.ToString(),
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsPersistent = true,
        };
        _db.UserSessions.Add(newSession);
        await _db.SaveChangesAsync();

        Response.Cookies.Append(SessionConstants.PersistentCookieName, newToken, BuildPersistentCookieOptions());

        var user = await _signInManager.UserManager.FindByIdAsync(match.UserId.ToString());
        if (user is null)
        {
            return AuthenticateResult.NoResult();
        }
        Context.Items[SessionConstants.PendingSessionItemKey] = newSessionId;
        await _signInManager.SignInAsync(user, isPersistent: false);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, match.UserId.ToString()),
                new Claim(SessionConstants.SessionIdClaim, newSessionId.ToString())
            },
            Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    private static CookieOptions BuildPersistentCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddDays(30),
    };
}
