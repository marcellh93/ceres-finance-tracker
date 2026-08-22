namespace ProjectCeres.Common;

/// <summary>
/// Application role names. Declared once here; `[Authorize(Roles = ...)]` takes a
/// literal string and cannot reference a constant, so the value is repeated in
/// attributes and pinned by AppRolesTests.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
}
