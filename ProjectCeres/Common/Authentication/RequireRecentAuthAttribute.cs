using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Apply on a controller action that requires a fresh password (or TOTP) proof within
/// the last 5 minutes. Backed by the "RecentAuth" authorization policy. Failed gate
/// returns 401 with error.code = "REAUTH_REQUIRED" via RecentAuthMiddlewareResultHandler.
/// Action-level only — see ReauthArchitectureTests.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequireRecentAuthAttribute : AuthorizeAttribute
{
    public const string PolicyName = "RecentAuth";
    public RequireRecentAuthAttribute() { Policy = PolicyName; }
}
