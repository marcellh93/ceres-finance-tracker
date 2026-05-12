using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Cookie auth event handler. On every authenticated request: read sid claim,
/// load matching UserSession, reject if missing/revoked, otherwise stamp
/// LastUsedAt = now (debounced to one write per 60 s per session row).
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
        // Cross-tenant by design: validates any session by ID during cookie auth — the HTTP context
        // principal is not yet committed when this event fires. Stage 10 architecture test allow-lists this file.
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .FirstOrDefaultAsync();

        if (session is null)
        {
            await RejectAsync(ctx);
            return;
        }

        // Stage 6b.3 Gap 9: debounce LastUsedAt writes to avoid hot-row contention under
        // authenticated load. 60-second resolution is sufficient for "last used" telemetry;
        // without the gate, every authenticated request issued an UPDATE, causing PostgreSQL
        // row-lock contention and authenticated write amplification.
        if (session.LastUsedAt < DateTime.UtcNow - TimeSpan.FromSeconds(60))
        {
            session.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
