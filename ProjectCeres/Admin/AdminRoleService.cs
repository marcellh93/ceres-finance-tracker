using Microsoft.AspNetCore.Identity;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Admin;

/// <summary>
/// Grants, revokes and reports the Admin role. Lives under Admin/ because
/// AnyAdminExistsAsync asks a question about every user, which no user-scoped
/// service is permitted to do (ADR-0065).
/// </summary>
public class AdminRoleService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager)
{
    /// <summary>Creates the Admin role row if it is not already present.</summary>
    public async Task EnsureRoleExistsAsync(CancellationToken ct = default)
    {
        if (await roleManager.RoleExistsAsync(AppRoles.Admin)) return;
        await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
    }

    /// <summary>Grants Admin. Returns false when the user does not exist.</summary>
    public async Task<bool> GrantAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        await EnsureRoleExistsAsync(ct);
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin)) return true;

        var result = await userManager.AddToRoleAsync(user, AppRoles.Admin);
        return result.Succeeded;
    }

    /// <summary>Revokes Admin. Returns false when the user does not exist.</summary>
    public async Task<bool> RevokeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var result = await userManager.RemoveFromRoleAsync(user, AppRoles.Admin);
        return result.Succeeded;
    }

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is not null && await userManager.IsInRoleAsync(user, AppRoles.Admin);
    }

    /// <summary>
    /// True when at least one account holds Admin. Used by the bootstrap tool to decide
    /// whether it may run outside Development.
    /// </summary>
    public async Task<bool> AnyAdminExistsAsync(CancellationToken ct = default)
    {
        if (!await roleManager.RoleExistsAsync(AppRoles.Admin)) return false;
        var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
        return admins.Count > 0;
    }
}
