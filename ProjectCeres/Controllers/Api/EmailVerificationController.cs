using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/email")]
public sealed class EmailVerificationController : ControllerBase
{
    private readonly EmailConfirmationService _service;

    public EmailVerificationController(EmailConfirmationService service) => _service = service;

    [HttpPost("verify"), AllowAnonymous, PreAuthCallSite("Email.Verify")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Verify([FromBody] EmailVerifyRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var outcome = await _service.ConfirmAsync(request.Token, HttpContext.RequestAborted);

        return outcome switch
        {
            EmailConfirmationConfirmOutcome.Success => NoContent(),
            EmailConfirmationConfirmOutcome.InvalidToken =>
                Unauthorized(new
                {
                    error = new
                    {
                        code = "INVALID_VERIFICATION_TOKEN",
                        message = "The verification link is invalid or has expired.",
                    }
                }),
            _ => throw new InvalidOperationException($"Unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    [HttpPost("verify/resend"), AllowAnonymous, PreAuthCallSite("Email.VerifyResend")]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    [ApplyEmailIpRateLimit]
    public async Task<IActionResult> RequestResend([FromBody] EmailVerifyResendRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var verifyUrlBase = $"{Request.Scheme}://{Request.Host}";

        try
        {
            await _service.RequestResendAsync(request.Email, verifyUrlBase, HttpContext.RequestAborted);
        }
        catch (EmailConfirmationService.RateLimitedException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = new { code = "RATE_LIMITED", message = "Too many requests. Please retry shortly." }
            });
        }

        return NoContent();
    }
}
