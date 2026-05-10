using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/reauth")]
[Authorize]
public sealed class ReauthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly TotpReplayGuard _replayGuard;

    public ReauthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TotpReplayGuard replayGuard)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _replayGuard = replayGuard;
    }

    [HttpPost]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthReauthByUser)]
    public async Task<IActionResult> Reauth([FromBody] ReauthRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return UnauthorizedEnvelope("UNAUTHENTICATED", "Authentication required.");

        if (await _userManager.IsLockedOutAsync(user))
            return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode))
                return UnprocessableEntity(new { error = new {
                    code = "VALIDATION_ERROR",
                    message = "TOTP code required.",
                    details = new[] { new { field = "totpCode", message = "TOTP code is required for accounts with MFA enabled." } }
                }});

            if (!MfaConstants.TotpCodeShape.IsMatch(request.TotpCode))
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");

            var ok = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, request.TotpCode);
            if (!ok)
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");

            var accepted = await _replayGuard.TryAcceptAsync(user.Id, request.TotpCode, HttpContext.RequestAborted);
            if (!accepted)
                return UnauthorizedEnvelope("INVALID_REAUTH", "The verification code is invalid or expired.");
        }
        else
        {
            if (string.IsNullOrEmpty(request.Password))
                return UnprocessableEntity(new { error = new {
                    code = "VALIDATION_ERROR",
                    message = "Password required.",
                    details = new[] { new { field = "password", message = "Password is required for accounts without MFA enabled." } }
                }});

            var ok = await _userManager.CheckPasswordAsync(user, request.Password);
            if (!ok)
            {
                await _userManager.AccessFailedAsync(user);
                if (await _userManager.IsLockedOutAsync(user))
                    return UnauthorizedEnvelope("ACCOUNT_LOCKED_OUT", "Account temporarily locked. Try again in 15 minutes.");
                return UnauthorizedEnvelope("INVALID_REAUTH", "Password is incorrect.");
            }
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        // Preserve the existing sid claim across the cookie refresh. Without this, the
        // claims factory finds no PendingSessionItemKey and the refreshed cookie omits
        // the sid claim — which then causes SessionRevocationValidator to reject the
        // session on the next request.
        var existingSidRaw = User.FindFirstValue(SessionConstants.SessionIdClaim);
        if (Guid.TryParse(existingSidRaw, out var existingSid))
        {
            HttpContext.Items[SessionConstants.PendingSessionItemKey] = existingSid;
        }

        HttpContext.Items[SessionConstants.LastReauthAtItemKey] =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        await _signInManager.RefreshSignInAsync(user);
        return NoContent();
    }

    private IActionResult UnauthorizedEnvelope(string code, string message)
        => Unauthorized(new { error = new { code, message } });
}
