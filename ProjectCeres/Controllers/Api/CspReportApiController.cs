using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/csp-report")]
public class CspReportApiController(ILogger<CspReportApiController> logger) : ControllerBase
{
    // No [FromBody]: browsers POST with Content-Type: application/csp-report
    // (report-uri) or application/reports+json (report-to), neither of which
    // MVC's default formatters accept — forcing formatter negotiation here
    // would 415 every real report. Reading the raw body accepts any content-type.
    [HttpPost, AllowAnonymous, EnableRateLimiting(AuthRateLimitPolicies.CspReportByIp)]
    public async Task<IActionResult> Report()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        logger.LogWarning("CSP violation report: {Report}", body);
        return NoContent();
    }
}
