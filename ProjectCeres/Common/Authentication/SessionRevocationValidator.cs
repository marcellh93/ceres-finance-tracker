using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Cookie auth event handler. On every authenticated request: read sid claim,
/// load matching UserSession, reject if missing/revoked, otherwise stamp
/// LastUsedAt = now. One DB read + one write per authenticated request —
/// future-work tracked in docs/planning-future.md § Session-validation perf.
/// </summary>
public static class SessionRevocationValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var sid = ctx.Principal?.FindFirstValue(SessionConstants.SessionIdClaim);
        if (!Guid.TryParse(sid, out var sessionId))
        {
            await RejectAsync(ctx);
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .FirstOrDefaultAsync();

        if (session is null)
        {
            await RejectAsync(ctx);
            return;
        }

        session.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
