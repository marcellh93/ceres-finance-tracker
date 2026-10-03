using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/health")]
public class HealthApiController : ControllerBase
{
    [HttpGet, AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpPost("validate"), AllowAnonymous]
    public IActionResult Validate([FromBody] ValidateRequest request) => Ok();

    // Stage 14.7 — reflects the post-ForwardedHeaders-middleware remote IP, so
    // tests can assert a spoofed X-Forwarded-For is rejected while KnownProxies
    // is empty (fails closed).
    [HttpGet("remote-ip"), AllowAnonymous]
    public IActionResult RemoteIp() => Content(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "");

    public class ValidateRequest
    {
        [Required]
        public string RequiredField { get; set; } = string.Empty;
    }
}
