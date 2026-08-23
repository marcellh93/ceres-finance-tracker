using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Admin;
using ProjectCeres.Models;

namespace ProjectCeres.Controllers.Api;

/// <summary>
/// Admin-only account administration. Promotion is how a second admin is created;
/// the first one is bootstrapped from the command line (see Tools/SeedDevUser.cs),
/// because there is no admin session available to authorize that first grant.
/// </summary>
/// <remarks>
/// Gated by <see cref="RequireAdminAttribute"/> at the class level, so every present and
/// future action on this controller is covered. See that attribute for why the project
/// does not use <c>[Authorize(Roles = "Admin")]</c>.
/// </remarks>
[ApiController]
[Route("api/admin/users")]
[RequireAdmin]
public class AdminUsersApiController(
    AdminRoleService adminRoles,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpPost("{id:guid}/promote")]
    public async Task<IActionResult> Promote(Guid id)
    {
        var granted = await adminRoles.GrantAsync(id, HttpContext.RequestAborted);
        return granted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Demote is idempotent: a target that already isn't an admin counts as success (204),
    /// not "not found", since the account itself may still exist. Demoting the last
    /// remaining admin is refused with 409 — it would lock out every admin surface,
    /// including the promote endpoint that would undo it. Self-demotion is allowed
    /// once another admin exists.
    /// </summary>
    [HttpPost("{id:guid}/demote")]
    public async Task<IActionResult> Demote(Guid id)
    {
        var ct = HttpContext.RequestAborted;

        var target = await userManager.FindByIdAsync(id.ToString());
        if (target is null) return NotFound();

        if (!await adminRoles.IsAdminAsync(id, ct)) return NoContent();

        if (await adminRoles.AdminCountAsync(ct) <= 1) return Conflict();

        await adminRoles.RevokeAsync(id, ct);
        return NoContent();
    }
}
