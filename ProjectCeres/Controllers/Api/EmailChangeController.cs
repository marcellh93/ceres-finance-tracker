using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/email-change")]
public sealed class EmailChangeController : ControllerBase
{
    private readonly EmailChangeService _service;

    public EmailChangeController(EmailChangeService service) => _service = service;

    // [RequireRecentAuth] inherits AuthorizeAttribute and registers the "RecentAuth"
    // policy, which is built on RequireAuthenticatedUser(). Adding a separate
    // [Authorize] attribute would cause AmbiguousMatchException in
    // ArchitectureTests.Every_controller_action_declares_authorization_intent (which
    // calls GetCustomAttribute<AuthorizeAttribute>() singularly), and is redundant.
    [HttpPost("request"), RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    [ApplyEmailIpRateLimit]
    public async Task<IActionResult> RequestChange([FromBody] EmailChangeRequest body)
    {
        var sid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sid is null || !Guid.TryParse(sid, out var userId))
        {
            return Unauthorized(new { error = new { code = "UNAUTHORIZED", message = "Authentication required." } });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();
        var verifyUrlBase = $"{Request.Scheme}://{Request.Host}";
        var revokeUrlBase = verifyUrlBase;

        try
        {
            var outcome = await _service.RequestAsync(
                userId, body.NewEmail, ip, ua, verifyUrlBase, revokeUrlBase, HttpContext.RequestAborted);

            return outcome switch
            {
                EmailChangeRequestOutcome.Accepted => Accepted(new
                {
                    data = new { message = "Verification email sent. Check your old and new inboxes." }
                }),
                EmailChangeRequestOutcome.EmailUnchanged => UnprocessableEntity(new
                {
                    error = new
                    {
                        code = "EMAIL_UNCHANGED",
                        message = "The new email matches the current address.",
                        details = Array.Empty<object>()
                    }
                }),
                EmailChangeRequestOutcome.EmailAlreadyInUse => UnprocessableEntity(new
                {
                    error = new
                    {
                        code = "EMAIL_ALREADY_IN_USE",
                        message = "That email is already registered to another account.",
                        details = Array.Empty<object>()
                    }
                }),
                _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
            };
        }
        catch (EmailChangeService.RateLimitedException ex)
        {
            Response.Headers.RetryAfter = ex.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = new { code = "RATE_LIMITED", message = "Too many requests. Please retry shortly." }
            });
        }
    }

    [HttpPost("confirm"), AllowAnonymous, PreAuthCallSite("EmailChange.ConfirmChange")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> ConfirmChange([FromBody] EmailChangeConfirmRequest body)
    {
        var outcome = await _service.ConfirmAsync(body.Token, HttpContext.RequestAborted);

        return outcome switch
        {
            EmailChangeConfirmOutcome.Success => NoContent(),
            EmailChangeConfirmOutcome.InvalidToken =>
                Unauthorized(new
                {
                    error = new
                    {
                        code = "INVALID_EMAIL_CHANGE_TOKEN",
                        message = "The email-change link is invalid or has expired."
                    }
                }),
            EmailChangeConfirmOutcome.EmailAlreadyInUse =>
                UnprocessableEntity(new
                {
                    error = new
                    {
                        code = "EMAIL_ALREADY_IN_USE",
                        message = "That email is already registered to another account.",
                        details = Array.Empty<object>()
                    }
                }),
            _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    [HttpPost("revoke"), AllowAnonymous, PreAuthCallSite("EmailChange.RevokeChange")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> RevokeChange([FromBody] EmailChangeRevokeRequest body)
    {
        var outcome = await _service.RevokeAsync(body.Token, HttpContext.RequestAborted);

        return outcome switch
        {
            EmailChangeRevokeOutcome.Success => NoContent(),
            EmailChangeRevokeOutcome.InvalidToken =>
                Unauthorized(new
                {
                    error = new
                    {
                        code = "INVALID_EMAIL_CHANGE_TOKEN",
                        message = "The email-change link is invalid or has expired."
                    }
                }),
            _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
        };
    }
}
