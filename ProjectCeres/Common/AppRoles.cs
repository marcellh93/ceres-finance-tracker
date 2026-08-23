namespace ProjectCeres.Common;

/// <summary>
/// Application role names. Declared once here; `[Authorize(Roles = ...)]` takes a
/// literal string and cannot reference a constant, so the value is repeated in
/// attributes and pinned by AppRolesTests.
/// </summary>
/// <remarks>
/// Do NOT guard an endpoint with <c>[Authorize(Roles = ...)]</c> yet. Role claims are
/// baked into the auth cookie at sign-in, and <c>SessionRevocationValidator</c> only
/// rejects principals — it never re-issues them — so a role granted after login stays
/// invisible for the rest of that session. Check membership live via
/// <c>AdminRoleService.IsAdminAsync</c> instead; see
/// <c>Controllers/Api/AdminUsersApiController.cs</c>. Refreshing claims mid-session is
/// scheduled for Stage 15.8.
/// </remarks>
public static class AppRoles
{
    public const string Admin = "Admin";
}
