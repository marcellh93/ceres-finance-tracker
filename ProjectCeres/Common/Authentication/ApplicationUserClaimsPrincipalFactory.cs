using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Adds the "sid" claim during sign-in. The login endpoint stages the SessionId
/// in HttpContext.Items[PendingSessionItemKey] before calling SignInAsync; this
/// factory copies it into the ticket. Without this hop, the SessionId would
/// have to be a column on ApplicationUser, which it is not.
/// </summary>
public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    private readonly IHttpContextAccessor _http;

    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> options,
        IHttpContextAccessor http)
        : base(userManager, roleManager, options)
    {
        _http = http;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var sessionId = _http.HttpContext?.Items[SessionConstants.PendingSessionItemKey] as Guid?;
        if (sessionId is { } sid)
        {
            identity.AddClaim(new Claim(SessionConstants.SessionIdClaim, sid.ToString()));
        }
        return identity;
    }
}
