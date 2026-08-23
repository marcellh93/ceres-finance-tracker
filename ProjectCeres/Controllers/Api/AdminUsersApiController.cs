using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
/// Admin membership is checked live via <see cref="AdminRoleService.IsAdminAsync"/>
/// rather than <c>[Authorize(Roles = "Admin")]</c>: role claims are baked into the
/// auth cookie at sign-in and this project's <c>OnValidatePrincipal</c> hook
/// (<c>SessionRevocationValidator</c>) never re-issues them, so a grant made after
/// login would be invisible to a claims-only check for the rest of that session.
/// </remarks>
[ApiController]
[Route("api/admin/users")]
[Authorize]
public class AdminUsersApiController(
    AdminRoleService adminRoles,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    private async Task<bool> CallerIsAdminAsync(CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var callerId) && await adminRoles.IsAdminAsync(callerId, ct);
    }

    [HttpPost("{id:guid}/promote")]
    public async Task<IActionResult> Promote(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        if (!await CallerIsAdminAsync(ct)) return Forbid();

        var granted = await adminRoles.GrantAsync(id, ct);
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
        if (!await CallerIsAdminAsync(ct)) return Forbid();

        var target = await userManager.FindByIdAsync(id.ToString());
        if (target is null) return NotFound();

        if (!await adminRoles.IsAdminAsync(id, ct)) return NoContent();

        if (await adminRoles.AdminCountAsync(ct) <= 1) return Conflict();

        await adminRoles.RevokeAsync(id, ct);
        return NoContent();
    }
}
