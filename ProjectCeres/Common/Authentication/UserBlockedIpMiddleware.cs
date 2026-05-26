using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Runs after authentication. For an authenticated request from an IP listed in
/// UserBlockedIps for the current user: revoke all matching UserSession rows
/// and return 403.
/// </summary>
public sealed class UserBlockedIpMiddleware
{
    private readonly RequestDelegate _next;

    public UserBlockedIpMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db, TimeProvider timeProvider)
    {
        if (context.User?.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "";
            var blocked = await db.UserBlockedIps
                .AnyAsync(b => b.UserId == userId && b.IpAddress == ip);
            if (blocked)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await db.UserSessions
                    .Where(s => s.UserId == userId && s.IpCreatedAt == ip && s.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now));

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }
}
