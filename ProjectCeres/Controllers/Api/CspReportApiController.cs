using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/csp-report")]
public class CspReportApiController(ILogger<CspReportApiController> logger) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting(AuthRateLimitPolicies.CspReportByIp)]
    public IActionResult Report([FromBody] object? report)
    {
        logger.LogWarning("CSP violation report: {Report}", report);
        return NoContent();
    }
}
