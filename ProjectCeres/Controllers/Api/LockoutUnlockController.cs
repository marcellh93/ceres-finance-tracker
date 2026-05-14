using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/lockout-unlock")]
public sealed class LockoutUnlockController : ControllerBase
{
    private readonly LockoutUnlockService _service;

    public LockoutUnlockController(LockoutUnlockService service) => _service = service;

    [HttpPost(""), AllowAnonymous, PreAuthCallSite("LockoutUnlock.Confirm")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> Confirm([FromBody] LockoutUnlockRequest request)
    {
        // [ApiController] + InvalidModelStateResponseFactory return 422 automatically
        // when ModelState is invalid — no manual ValidationProblem check needed.

        var outcome = await _service.ConfirmAsync(request.Token, HttpContext.RequestAborted);
        return outcome switch
        {
            LockoutUnlockOutcome.Success => NoContent(),
            LockoutUnlockOutcome.InvalidToken =>
                Unauthorized(new { error = new { code = "INVALID_LOCKOUT_UNLOCK_TOKEN",
                                                  message = "The unlock link is invalid or expired." } }),
            _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
        };
    }
}
