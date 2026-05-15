using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;

namespace ProjectCeres.Controllers.Api;

/// <summary>
/// Receives Resend's Svix-signed webhook deliveries. Stage 8e.
///
/// <para>
/// Route prefix <c>/api/internal/</c> is reserved for endpoints that are NOT part of
/// the user-facing API surface: they are called by infrastructure (here: Resend's
/// webhook fanout) and authenticated by a non-cookie mechanism (here: Svix HMAC).
/// Future internal-only endpoints should follow the same prefix.
/// </para>
/// </summary>
[ApiController]
[Route("api/internal/email-webhook")]
public sealed class ResendWebhookController : ControllerBase
{
    private readonly IResendSignatureVerifier _verifier;
    private readonly IOptions<EmailOptions> _opts;
    private readonly AppDbContext _db;
    private readonly ILogger<ResendWebhookController> _logger;

    public ResendWebhookController(
        IResendSignatureVerifier verifier,
        IOptions<EmailOptions> opts,
        AppDbContext db,
        ILogger<ResendWebhookController> logger)
    {
        _verifier = verifier;
        _opts = opts;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a Resend delivery event. Verifies the Svix signature, persists the event
    /// to <c>EmailDeliveryEvents</c>, and on <c>email.bounced</c> flips
    /// <c>EmailConfirmed = false</c> for the matching user (if one exists).
    /// </summary>
    [HttpPost("resend")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [PreAuthCallSite("ResendWebhook.Receive")]
    [ApplyEmailIpRateLimit]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Buffer the body so we can read it raw (for HMAC) and then re-bind it for
        // anything downstream. Resend's signature is over the byte-exact request body —
        // any reformatting after this point would invalidate the comparison.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        string body;
        using (var reader = new StreamReader(Request.Body, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(ct);
        }
        Request.Body.Position = 0;

        var secret = _opts.Value.Resend.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Misconfiguration — bail out as 401 (treating an unsigned-by-policy request
            // as unauthorized) but log loudly: an operator-misconfigured endpoint
            // silently dropping signed events would be very hard to notice.
            _logger.LogError("Resend webhook received but Email:Resend:WebhookSecret is not configured.");
            return Unauthorized();
        }

        var svixId = Request.Headers["Svix-Id"].ToString();
        var svixTs = Request.Headers["Svix-Timestamp"].ToString();
        var svixSig = Request.Headers["Svix-Signature"].ToString();
        if (string.IsNullOrEmpty(svixId) || string.IsNullOrEmpty(svixTs) || string.IsNullOrEmpty(svixSig))
            return Unauthorized();
        if (!_verifier.Verify(svixId, svixTs, body, svixSig, secret))
            return Unauthorized();

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return BadRequest();
        var type = typeEl.GetString()!;

        string? emailAddress = null;
        string? messageId = null;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.TryGetProperty("to", out var toEl)
                && toEl.ValueKind == JsonValueKind.Array
                && toEl.GetArrayLength() > 0)
            {
                emailAddress = toEl[0].GetString();
            }
            if (data.TryGetProperty("email_id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                messageId = idEl.GetString();
        }
        emailAddress ??= "";
        messageId ??= "";

        // Cross-tenant lookup by normalized email. The controller is pre-auth, so EF's
        // per-tenant global query filter would zero-rows this — IgnoreQueryFilters is
        // the documented escape hatch (allow-listed in ArchitectureTests).
        Guid? userId = null;
        string normalized = emailAddress.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(normalized))
        {
            userId = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.NormalizedEmail == normalized)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(ct);
        }

        _db.Set<EmailDeliveryEvent>().Add(new EmailDeliveryEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MessageId = messageId,
            Type = type,
            EmailAddress = emailAddress,
            Payload = body,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        if (type == "email.bounced" && userId is not null)
        {
            var user = await _db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (user is not null)
            {
                user.EmailConfirmed = false;
            }
        }

        await _db.SaveChangesAsync(ct);

        // Deliberately log only the type + message id (no body, no email, no secret).
        _logger.LogInformation("Resend webhook {Type} processed (messageId={MessageId})", type, messageId);
        return NoContent();
    }
}
