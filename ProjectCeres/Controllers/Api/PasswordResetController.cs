using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/password-reset")]
public sealed class PasswordResetController : ControllerBase
{
    private readonly PasswordResetService _service;

    public PasswordResetController(PasswordResetService service) => _service = service;

    [HttpPost("request"), AllowAnonymous]
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
}
