namespace ProjectCeres.Common;

/// <summary>
/// Application role names. Declared once here and pinned by AppRolesTests, which keeps
/// this constant in sync with the literal the role row carries in the database.
/// </summary>
/// <remarks>
/// Never guard an endpoint with <c>[Authorize(Roles = ...)]</c>. Role claims are baked
/// into the auth cookie at sign-in and <c>SessionRevocationValidator</c> only rejects
/// principals — it never re-issues them — so a role granted after login stays invisible,
/// and a role revoked after login is still honoured until the cookie expires.
/// Use <c>[RequireAdmin]</c> at the class level instead (ProjectCeres/Admin/), which
/// checks the role live per request. Class level, never per action: a per-action check
/// is satisfiable by omission. See ADR-0080.
/// </remarks>
public static class AppRoles
{
    public const string Admin = "Admin";
}
