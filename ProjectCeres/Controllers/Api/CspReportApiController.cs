using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/csp-report")]
public class CspReportApiController(ILogger<CspReportApiController> logger) : ControllerBase
{
    // Real reports are a few hundred bytes; browsers truncate long ones themselves.
    private const int MaxBodyBytes = 16 * 1024;
    private const int MaxLoggedChars = 2048;

    // No [FromBody]: browsers POST with Content-Type: application/csp-report
    // (report-uri) or application/reports+json (report-to), neither of which
    // MVC's default formatters accept — forcing formatter negotiation here
    // would 415 every real report. Reading the raw body accepts any content-type.
    //
    // [IgnoreAntiforgeryToken]: the app registers a global AutoValidateAntiforgeryToken
    // filter (Program.cs). A browser sends CSP violation reports via its own reporting
    // mechanism with NO antiforgery token, so without this the global filter 400s every
    // real report before it reaches this action. Safe: the endpoint is anonymous and
    // only logs — it mutates no state, so CSRF protection is meaningless here.
    //
    // The endpoint is anonymous, so the body is attacker-controlled: capped at MaxBodyBytes, and
    // only a bounded single-line prefix is logged so it cannot flood or forge log entries.
    [HttpPost, AllowAnonymous, IgnoreAntiforgeryToken, EnableRateLimiting(AuthRateLimitPolicies.CspReportByIp)]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Report()
    {
        using var reader = new StreamReader(Request.Body);
        var buffer = new char[MaxLoggedChars + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);

        var report = SingleLine(buffer.AsSpan(0, Math.Min(read, MaxLoggedChars)));
        if (read > MaxLoggedChars) report += " …[truncated]";

        logger.LogWarning("CSP violation report: {Report}", report);
        return NoContent();
    }

    private static string SingleLine(ReadOnlySpan<char> text)
    {
        var chars = text.ToArray();
        for (var i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i])) chars[i] = ' ';
        return new string(chars);
    }
}
