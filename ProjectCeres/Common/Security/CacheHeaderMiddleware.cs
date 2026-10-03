using Microsoft.Extensions.Primitives;

namespace ProjectCeres.Common.Security;

/// <summary>Path-aware cache headers (Stage 14.8). Static /dist/* keeps its long cache;
/// API + authenticated responses are no-store; logout additionally clears site data.</summary>
public sealed class CacheHeaderMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/dist"))
                return Task.CompletedTask; // defensive no-op: UseStaticFiles normally handles /dist first

            if ((path.StartsWithSegments("/api") || ctx.User.Identity?.IsAuthenticated == true)
                && !ctx.Response.Headers.ContainsKey("Cache-Control"))
                ctx.Response.Headers.CacheControl = "private, no-store";

            if (path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
                ctx.Response.Headers["Clear-Site-Data"] = new StringValues("\"cache\", \"cookies\", \"storage\"");

            return Task.CompletedTask;
        });
        await next(ctx);
    }
}
