using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public Task<IActionResult> Register() => throw new NotImplementedException();

    [HttpPost("login"), AllowAnonymous]
    public Task<IActionResult> Login() => throw new NotImplementedException();

    [HttpPost("logout"), Authorize]
    public Task<IActionResult> Logout() => throw new NotImplementedException();
}
