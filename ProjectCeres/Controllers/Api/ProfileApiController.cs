using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

/// <summary>
/// Stage 13.8 Task 8 — GDPR data-export request + download. The download endpoint is
/// the highest-risk surface in this feature: it hands over a full personal-data ZIP.
/// </summary>
[ApiController]
[Route("api/profile")]
public sealed class ProfileApiController(
    IExportJobService exportJobs,
    TokenLookupHasher lookupHasher,
    ExportTokenGenerator tokenGenerator,
    TimeProvider clock) : ControllerBase
{
    [HttpPost("export"), RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.ProfileExportByUser)]
    public async Task<IActionResult> RequestExport([FromBody] ProfileExportRequest? body)
    {
        if (body?.Format is not null && !string.Equals(body.Format, "zip", StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Unsupported export format.",
                    details = new[] { new { field = "format", message = "Only \"zip\" is supported." } }
                }
            });
        }

        var job = await exportJobs.CreateOrGetPendingAsync(HttpContext.RequestAborted);

        return Accepted(new
        {
            data = new
            {
                jobId = job.Id,
                message = "Export started. You will be notified by email when ready."
            }
        });
    }

    // [Authorize] ONLY — no [RequireRecentAuth]. The download token is the second
    // factor; forcing reauth on an email-link click is hostile (D6 dual gate).
    [HttpGet("export/download"), Authorize]
    public async Task<IActionResult> DownloadExport([FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token)) return NotFound();

        var lookup = lookupHasher.ComputeLookup(token);
        var job = await exportJobs.FindOwnByTokenAsync(lookup, HttpContext.RequestAborted);
        // RLS makes a foreign user's job invisible — null here is indistinguishable
        // from "no such token", so this stays 404, never 403.
        if (job is null) return NotFound();

        // Two-factor: TokenLookup only narrows to a candidate row. A forged or
        // colliding lookup must still fail the Argon2id verification below.
        if (!tokenGenerator.Verify(token, job.TokenHash)) return NotFound();

        if (job.Status != Models.ExportJobStatus.Ready) return NotFound();

        // Already consumed, or the download window lapsed: the job existed and the
        // token verified, so this is "gone", not "not found".
        var expired = job.ExpiresAt is null || job.ExpiresAt <= clock.GetUtcNow().UtcDateTime;
        if (job.ConsumedAt is not null || expired) return GoneEnvelope();

        // A cleaned-up Ready row can have a null StoredPath once the worker has swept
        // an expired/consumed job's file — check it explicitly, not just Status.
        if (job.StoredPath is null) return GoneEnvelope();

        await exportJobs.MarkConsumedAsync(job, HttpContext.RequestAborted);

        return PhysicalFile(job.StoredPath, "application/zip", "ceres-data-export.zip");
    }

    private static IActionResult GoneEnvelope() => new ObjectResult(new
    {
        error = new
        {
            code = "EXPORT_LINK_EXPIRED",
            message = "This export link is no longer valid. Please request a fresh export."
        }
    })
    { StatusCode = StatusCodes.Status410Gone };
}
