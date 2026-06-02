using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Cookie auth event handler. On every authenticated request: read sid claim,
/// load matching UserSession, reject if missing/revoked, otherwise stamp
/// LastUsedAt = now (debounced to one write per 60 s per session row).
/// </summary>
[RequiresAdminContext]
public static class SessionRevocationValidator
{
    [AllowsWallClock("static security-stamp revalidation helper called from cookie validation; TimeProvider injection requires conversion to instance class registered in DI — out of 9.5c scope")]
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var sid = ctx.Principal?.FindFirstValue(SessionConstants.SessionIdClaim);
        if (!Guid.TryParse(sid, out var sessionId))
        {
            await RejectAsync(ctx);
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<AdminDbContext>();
        // Uses AdminDbContext because cookie validation fires BEFORE the auth pipeline
        // has established the user context, so the RLS GUC `app.current_user_ref` is
        // unset. With the runtime AppDbContext, RLS would evaluate the user_isolation
        // policy against an unset GUC and return zero rows — even for valid sessions —
        // causing the validator to reject every authenticated request. The admin role
        // (ceres_admin, BYPASSRLS) sees the row directly. Same pattern as UserJobRunner.
        // IgnoreQueryFilters() bypasses the EF-level UserId filter; AdminDbContext's
        // ceres_admin role bypasses the PostgreSQL-level RLS policy. Both layers are
        // required: EF filter is in-process, RLS policy is in the database.
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
