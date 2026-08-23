using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace ProjectCeres.Admin;

/// <summary>
/// Apply on an admin-only controller or action. Backed by the "AdminLive" policy, which
/// reads role membership from the database on every request rather than from the auth
/// cookie. Failed gate returns 403 for a signed-in non-admin, 401 for anonymous.
/// </summary>
/// <remarks>
/// Deliberately not <c>[Authorize(Roles = "Admin")]</c>: role claims are baked into the
/// cookie at sign-in and <c>SessionRevocationValidator</c> never re-issues the principal,
/// so a role granted after login would be invisible and a role revoked after login would
/// still be honoured until the cookie expired.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireAdminAttribute : AuthorizeAttribute
{
    public const string PolicyName = "AdminLive";
    public RequireAdminAttribute() { Policy = PolicyName; }
}

/// <summary>Requirement paired with <see cref="RequireAdminAttribute"/>.</summary>
public sealed class AdminLiveRequirement : IAuthorizationRequirement;

/// <summary>
/// Resolves the caller's id from the principal and asks the database whether they hold
/// Admin right now. Registered as a singleton, so the scoped AdminRoleService is
/// resolved from the current request scope.
/// </summary>
public sealed class AdminLiveRequirementHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<AdminLiveRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminLiveRequirement requirement)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var callerId)) return;

        var http = httpContextAccessor.HttpContext;
        if (http is null) return;

        var adminRoles = http.RequestServices.GetRequiredService<AdminRoleService>();
        if (await adminRoles.IsAdminAsync(callerId, http.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
