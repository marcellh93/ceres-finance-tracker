using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Sessions;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsApiController(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [RequireRecentAuth]
    public async Task<ActionResult<SessionDto[]>> List()
    {
        var userId = currentUser.UserId;
        var currentSid = ParseCurrentSid();

        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id,
                s.CreatedAt,
                s.LastUsedAt,
                s.IpCreatedAt,
                s.UserAgent,
                s.Id == currentSid))
            .ToArrayAsync();

        return Ok(sessions);
    }

    [HttpDelete("{id:guid}")]
    [RequireRecentAuth]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var userId = currentUser.UserId;

        var session = await db.UserSessions
            .Where(s => s.UserId == userId && s.Id == id)
            .SingleOrDefaultAsync();

        if (session is null) return NotFound();

        session.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("block-ip")]
    [RequireRecentAuth]
    public async Task<IActionResult> BlockIp([FromBody] BlockIpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "IpAddress is required.",
                    details = Array.Empty<object>()
                }
            });
        }

        var userId = currentUser.UserId;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        db.UserBlockedIps.Add(new UserBlockedIp
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IpAddress = request.IpAddress,
            BlockedAt = now,
        });

        await db.UserSessions
            .Where(s => s.UserId == userId && s.IpCreatedAt == request.IpAddress && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));

        await db.SaveChangesAsync();
        return NoContent();
    }

    private Guid ParseCurrentSid()
    {
        var raw = User.FindFirstValue(SessionConstants.SessionIdClaim);
        return Guid.TryParse(raw, out var sid) ? sid : Guid.Empty;
    }
}
