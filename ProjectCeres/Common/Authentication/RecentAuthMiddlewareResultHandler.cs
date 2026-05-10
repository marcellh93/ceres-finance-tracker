using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Wraps the framework default authorization middleware result handler. When authorization
/// fails specifically because of RecentAuthRequirement, emits 401 with the
/// REAUTH_REQUIRED envelope. All other failures (e.g. unauthenticated requests against
/// the global RequireAuthenticatedUser fallback) delegate to the default handler so the
/// existing OnRedirectToLogin → 401 path is preserved.
/// </summary>
public sealed class RecentAuthMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context,
        AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailedRequirements
                .Any(r => r is RecentAuthRequirement) == true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "REAUTH_REQUIRED",
                    message = "Please confirm your identity to continue.",
                }
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
