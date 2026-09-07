using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Services;
using ProjectCeres.ViewModels.Sessions;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsApiController(ISessionService sessions) : ControllerBase
{
    [HttpGet]
    [RequireRecentAuth]
    public async Task<ActionResult<SessionDto[]>> List()
    {
        var result = await sessions.GetActiveAsync(ParseCurrentSid());
        return Ok(result.ToArray());
    }

    [HttpDelete("{id:guid}")]
    [RequireRecentAuth]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var result = await sessions.TryRevokeAsync(id);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpPost("block-ip")]
    [RequireRecentAuth]
    public async Task<IActionResult> BlockIp([FromBody] BlockIpRequest request)
    {
        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await sessions.TryBlockIpAsync(request.IpAddress, callerIp);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    [HttpGet("blocked-ips")]
    [RequireRecentAuth]
    public async Task<ActionResult<BlockedIpDto[]>> BlockedIps()
    {
        var result = await sessions.GetBlockedIpsAsync();
        return Ok(result.ToArray());
    }

    [HttpDelete("blocked-ips")]
    [RequireRecentAuth]
    public async Task<IActionResult> UnblockIp([FromBody] UnblockIpRequest request)
    {
        var result = await sessions.TryUnblockIpAsync(request.IpAddress);
        return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
    }

    private IActionResult ToErrorResponse(ResultError error) => error.Code switch
    {
        "NOT_FOUND" => NotFound(),
        // 409, not 422: the request is well-formed and the IP is valid — it is the
        // current state (you are connected from it) that makes it refusable.
        "SELF_LOCKOUT" => Conflict(new
        {
            error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() }
        }),
        _ => UnprocessableEntity(new
        {
            error = new { code = error.Code, message = error.Message, details = Array.Empty<object>() }
        }),
    };

    private Guid ParseCurrentSid()
    {
        var raw = User.FindFirstValue(SessionConstants.SessionIdClaim);
        return Guid.TryParse(raw, out var sid) ? sid : Guid.Empty;
    }
}
