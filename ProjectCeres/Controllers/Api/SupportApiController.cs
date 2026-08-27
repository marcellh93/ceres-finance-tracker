using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/support")]
[Authorize]
public class SupportApiController(
    ISupportTicketService tickets,
    IFileAttachmentService attachments,
    IAuditLogWriter auditLog,
    IEmailComposer composer,
    IEmailService email,
    ISupportRecipientResolver supportRecipient,
    ICurrentUserAccessor currentUser,
    ILogger<SupportApiController> logger) : ControllerBase
{
    // Validation attributes target the PARAMETER, not the property. On a record primary
    // constructor, [property: Required] lands where model binding does not look, and
    // ASP.NET throws at request time rather than quietly skipping the rule.
    public sealed record CreateTicketRequest(
        [Required, StringLength(200, MinimumLength = 1)] string Subject,
        [Required, StringLength(5000, MinimumLength = 1)] string Message,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId);

    public sealed record AttachmentDto(
        Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTime UploadedAt);

    public sealed record TicketDto(
        Guid Id,
        string Subject,
        string Message,
        SupportTicketStatus Status,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<AttachmentDto> Attachments);

    // Every successful create sends mail to the support mailbox, so this endpoint is
    // an email-triggering one and security-model.md § Email Security Rules requires it
    // be rate-limited: "Rate-limit ALL email-triggering endpoints ... to prevent the app
    // being used as a spam relay." Without this, one authenticated user can loop the
    // endpoint and flood the operator's inbox from our own domain, costing sender
    // reputation and burying real tickets.
    //
    // EmailByUser partitions on the body's email when present and falls back to the
    // authenticated user id — this body has no email field, so it keys on the user, which
    // is the cap we want. ApplyEmailIpRateLimit adds the per-IP backstop.
    [HttpPost("tickets")]
    [ApplyEmailIpRateLimit]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        SupportTicket ticket;
        try
        {
            ticket = await tickets.CreateAsync(
                request.Subject, request.Message, request.Priority, request.PrecedingTicketId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Validation(ex);
        }

        await auditLog.RecordAsync(currentUser.UserId, AuditLogAction.SupportTicketCreated, ct: ct);
        await NotifySupportAsync(ticket, ct);

        return CreatedAtAction(nameof(GetOne), new { ticketId = ticket.Id }, new { id = ticket.Id });
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var own = await tickets.ListOwnAsync(ct);
        return Ok(own.Select(t => ToDto(t, [])));
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<IActionResult> GetOne(Guid ticketId, CancellationToken ct)
    {
        var ticket = await tickets.GetOwnAsync(ticketId, ct);
        // 404 rather than 403: a ticket that is not yours must not be distinguishable
        // from one that does not exist.
        return ticket is null ? NotFound() : Ok(ToDto(ticket, ticket.Attachments));
    }

    [HttpPost("tickets/{ticketId:guid}/close")]
    public async Task<IActionResult> Close(Guid ticketId, CancellationToken ct)
    {
        try
        {
            await tickets.CloseAsync(ticketId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Validation(ex);
        }
    }

    [HttpPost("tickets/{ticketId:guid}/attachments")]
    [RequestSizeLimit(11 * 1024 * 1024)] // 10 MB payload + multipart overhead
    public async Task<IActionResult> Upload(Guid ticketId, IFormFile file, CancellationToken ct)
    {
        try
        {
            var saved = await attachments.UploadForSupportTicketAsync(ticketId, file);
            return Ok(new AttachmentDto(
                saved.Id, saved.FileName, saved.ContentType, saved.FileSizeBytes, saved.UploadedAt));
        }
        catch (InvalidOperationException ex)
        {
            return Validation(ex);
        }
    }

    private static TicketDto ToDto(SupportTicket t, IEnumerable<SupportTicketAttachment> files) =>
        new(t.Id, t.Subject, t.Message, t.Status, t.Priority, t.PrecedingTicketId,
            t.CreatedAt, t.UpdatedAt,
            [.. files.Select(a => new AttachmentDto(
                a.Id, a.FileName, a.ContentType, a.FileSizeBytes, a.UploadedAt))]);

    private IActionResult Validation(InvalidOperationException ex) =>
        UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                details = Array.Empty<object>()
            }
        });

    /// <summary>
    /// Emails the configured support address. Failures are logged and swallowed: the
    /// ticket is already committed, and a mail outage must not tell the user their report
    /// failed when it did not. Production cannot reach the unconfigured branch — Program.cs
    /// refuses to boot without Email:SupportAddress.
    /// </summary>
    private async Task NotifySupportAsync(SupportTicket ticket, CancellationToken ct)
    {
        var recipient = supportRecipient.Resolve();
        if (recipient is null)
        {
            logger.LogInformation(
                "Support ticket {TicketId} filed; no Email:SupportAddress configured, so no notification was sent.",
                ticket.Id);
            return;
        }

        try
        {
            var message = composer.Compose(
                EmailTemplateKey.SupportTicketReceived,
                CultureInfo.CurrentUICulture,
                ticket.Priority.ToString(),
                ticket.Subject,
                User.Identity?.Name ?? currentUser.UserId.ToString(),
                ticket.Id.ToString(),
                ticket.Message) with { To = recipient };
            await email.SendAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Support ticket {TicketId} was filed but the notification email failed.", ticket.Id);
        }
    }
}
