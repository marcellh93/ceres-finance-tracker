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

    public class ValidateRequest
    {
        [Required]
        public string RequiredField { get; set; } = string.Empty;
    }
}
