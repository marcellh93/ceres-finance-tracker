using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;
using System.Linq;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/password-reset")]
public sealed class PasswordResetController : ControllerBase
{
    private readonly PasswordResetService _service;

    public PasswordResetController(PasswordResetService service) => _service = service;

    [HttpPost("request"), AllowAnonymous, PreAuthCallSite("PasswordReset.RequestReset")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> RequestReset([FromBody] PasswordResetRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();
        var resetUrlBase = $"{Request.Scheme}://{Request.Host}";

        try
        {
            await _service.RequestAsync(request.Email, ip, ua, resetUrlBase, HttpContext.RequestAborted);
        }
        catch (PasswordResetService.RateLimitedException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = new { code = "RATE_LIMITED", message = "Too many requests. Please retry shortly." }
            });
        }

        return NoContent();
    }

    [HttpPost("confirm"), AllowAnonymous, PreAuthCallSite("PasswordReset.Confirm")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Confirm([FromBody] PasswordResetConfirmRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var outcome = await _service.ConfirmAsync(
            request.Token, request.NewPassword, request.TotpCode, HttpContext.RequestAborted);

        return outcome switch
        {
            PasswordResetConfirmOutcome.Success => NoContent(),
            PasswordResetConfirmOutcome.RequiresTotp => Ok(new { requiresTotp = true }),
            PasswordResetConfirmOutcome.InvalidToken =>
                Unauthorized(new { error = new { code = "INVALID_RESET_TOKEN", message = "The reset link is invalid or has expired." } }),
            PasswordResetConfirmOutcome.InvalidTotp =>
                Unauthorized(new { error = new { code = "INVALID_MFA_CODE", message = "The verification code is invalid or expired." } }),
            PasswordResetConfirmOutcome.PasswordPolicyViolation policyOutcome =>
                UnprocessableEntity(new
                {
                    error = new
                    {
                        code = "VALIDATION_ERROR",
                        message = "The new password does not meet the policy.",
                        details = policyOutcome.Errors
                            .Select(e => new { field = "newPassword", message = e.Description })
                            .ToArray(),
                    }
                }),
            _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
        };
    }
}
