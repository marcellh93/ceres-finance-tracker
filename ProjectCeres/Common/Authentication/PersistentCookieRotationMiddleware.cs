using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Runs before UseAuthentication. If the request has a __Host-Persist cookie but
/// no __Host-Session cookie, look up matching UserSession by hashed token, rotate
/// (issue new + replace hash + clear old cookie), insert a new UserSession row,
/// sign the user into the Identity scheme so the response carries a fresh
/// __Host-Session cookie. Subsequent authentication picks up the rotated cookie
/// via SignInManager.SignInAsync's response-side write — and for THIS request,
/// HttpContext.User is set directly so authorization succeeds without a round-trip.
/// </summary>
public sealed class PersistentCookieRotationMiddleware
{
    private readonly RequestDelegate _next;

    public PersistentCookieRotationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        PersistentTokenService tokens,
        SignInManager<ApplicationUser> signInManager)
    {
        if (context.Request.Cookies.ContainsKey(SessionConstants.SessionCookieName))
        {
            await _next(context);
            return;
        }
        if (!context.Request.Cookies.TryGetValue(SessionConstants.PersistentCookieName, out var rawToken)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            await _next(context);
            return;
        }

        var candidates = await db.UserSessions
            .Where(s => s.IsPersistent && s.RevokedAt == null && s.PersistentTokenHash != null)
            .ToListAsync();

        var match = candidates.FirstOrDefault(c => tokens.Verify(rawToken, c.PersistentTokenHash!));
        if (match is null)
        {
            await _next(context);
            return;
        }

        // Rotate.
        match.RevokedAt = DateTime.UtcNow;

        var newSessionId = Guid.NewGuid();
        var newToken = tokens.Generate();
        var newSession = new UserSession
        {
            Id = newSessionId,
            UserId = match.UserId,
            PersistentTokenHash = tokens.Hash(newToken),
            IpCreatedAt = context.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsPersistent = true,
        };
        db.UserSessions.Add(newSession);
        await db.SaveChangesAsync();

        context.Response.Cookies.Append(
            SessionConstants.PersistentCookieName,
            newToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(30),
            });

        var user = await signInManager.UserManager.FindByIdAsync(match.UserId.ToString());
        if (user is null)
        {
            await _next(context);
            return;
        }

        // Sign into Identity (writes __Host-Session response cookie for next request).
        context.Items[SessionConstants.PendingSessionItemKey] = newSessionId;
        await signInManager.SignInAsync(user, isPersistent: false);

        // Stamp the current request's principal so authorization succeeds for this hop.
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, match.UserId.ToString()),
                new Claim(SessionConstants.SessionIdClaim, newSessionId.ToString())
            },
            authenticationType: SessionConstants.PersistentScheme);
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
