using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

/// <summary>
/// Stage 13.8 Task 8 — GDPR data-export request + download. Stage 13.9 Task 9 adds the
/// right-to-erasure request/cancel pair. The download and erasure endpoints are the
/// highest-risk surfaces in this controller: one hands over a full personal-data ZIP,
/// the other irreversibly seals an account.
/// </summary>
[ApiController]
[Route("api/profile")]
public sealed class ProfileApiController(
    IExportJobService exportJobs,
    TokenLookupHasher lookupHasher,
    ExportTokenGenerator tokenGenerator,
    IErasureService erasureService,
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

    [HttpPost("erasure"), RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.ProfileErasureByUser)]
    public async Task<IActionResult> RequestErasure([FromBody] ErasureRequestBody? body)
    {
        if (!string.Equals(body?.Confirm, "ERASE", StringComparison.Ordinal))
        {
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Type ERASE to confirm account erasure.",
                    details = new[] { new { field = "confirm", message = "Must be exactly \"ERASE\"." } }
                }
            });
        }

        // RequestAsync internally dedupes an existing Sealed request (returns it with
        // an empty raw token) — a repeat call is safe and idempotent, no branching needed here.
        await erasureService.RequestAsync(HttpContext.RequestAborted);

        return Accepted(new
        {
            data = new
            {
                message = "Erasure scheduled. Check your email for a link to cancel within 72 hours."
            }
        });
    }

    [HttpPost("erasure/cancel"), AllowAnonymous, PreAuthCallSite("Profile.ErasureCancel")]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthLoginByIp)]
    public async Task<IActionResult> CancelErasure([FromBody] ErasureCancelRequest body)
    {
        var outcome = await erasureService.CancelAsync(body.Token, HttpContext.RequestAborted);

        return outcome switch
        {
            Services.ErasureCancelOutcome.Cancelled => NoContent(),
            Services.ErasureCancelOutcome.NotFound => NotFound(),
            Services.ErasureCancelOutcome.Gone => ErasureGoneEnvelope(),
            _ => throw new InvalidOperationException($"unhandled outcome: {outcome.GetType().Name}"),
        };
    }

    private static IActionResult ErasureGoneEnvelope() => new ObjectResult(new
    {
        error = new
        {
            code = "ERASURE_LINK_EXPIRED",
            message = "This erasure cancellation link is no longer valid."
        }
    })
    { StatusCode = StatusCodes.Status410Gone };
}
