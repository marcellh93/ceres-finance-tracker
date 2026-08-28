using System.ComponentModel.DataAnnotations;
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
    ISupportMessageService messages,
    IFileAttachmentService attachments,
    IAuditLogWriter auditLog,
    ISupportNotificationService notify,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    // Validation attributes target the PARAMETER, not the property. On a record primary
    // constructor, [property: Required] lands where model binding does not look, and
    // ASP.NET throws at request time rather than quietly skipping the rule.
    public sealed record CreateTicketRequest(
        [Required, StringLength(200, MinimumLength = 1)] string Subject,
        [Required, StringLength(5000, MinimumLength = 1)] string Message,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId);

    public sealed record ReplyRequest(
        [Required, StringLength(5000, MinimumLength = 1)] string Body);

    public sealed record AttachmentDto(
        Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTime UploadedAt);

    public sealed record MessageDto(
        Guid Id,
        SupportMessageAuthor AuthorRole,
        string Body,
        DateTime CreatedAt,
        IReadOnlyList<AttachmentDto> Attachments);

    // The list item and the thread are two different shapes. The list is a rollup — one row
    // per ticket with a message count, no bodies. The thread is the full conversation.
    public sealed record TicketListItemDto(
        Guid Id,
        string Subject,
        SupportTicketStatus Status,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int MessageCount,
        DateTime LastMessageAt);

    public sealed record TicketThreadDto(
        Guid Id,
        string Subject,
        SupportTicketStatus Status,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<MessageDto> Messages);

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

        await auditLog.RecordAsync(currentUser.UserId, AuditLogAction.SupportTicketCreated,
            nameof(SupportTicket), ticket.Id, ct);
        await notify.NotifyOperatorOfNewTicketAsync(ticket, FromDisplay(), ct);

        // Return the first message's id alongside the ticket id: the client attaches any
        // buffered files by posting them to that message (create is text + optional
        // attachments, and the attachment hangs off a message, not the ticket).
        var firstMessageId = ticket.Messages.Single().Id;
        return CreatedAtAction(nameof(GetOne), new { ticketId = ticket.Id },
            new { id = ticket.Id, firstMessageId });
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var own = await tickets.ListOwnAsync(ct);
        return Ok(own.Select(t => new TicketListItemDto(
            t.Id, t.Subject, t.Status, t.Priority, t.PrecedingTicketId,
            t.CreatedAt, t.UpdatedAt, t.MessageCount, t.LastMessageAt)));
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<IActionResult> GetOne(Guid ticketId, CancellationToken ct)
    {
        var ticket = await messages.GetThreadAsync(ticketId, ct);
        // 404 rather than 403: a ticket that is not yours must not be distinguishable
        // from one that does not exist. GetThreadAsync scopes to the caller and returns
        // null for a ticket that is absent or foreign — the same answer, which is the point.
        return ticket is null ? NotFound() : Ok(ToThreadDto(ticket));
    }

    // POST a user reply onto one of the caller's own tickets. The state machine (not this
    // controller) decides whether the ticket's status allows a reply — a Closed ticket
    // throws, and the throw becomes the 422 Validation envelope. On success the ticket
    // returns to Open (any user reply hands the ball back to the operator).
    //
    // Rate-limited like Create: a reply notifies the operator, so it is an email-triggering
    // endpoint and security-model.md § Email Security Rules requires the cap. EmailByUser has
    // no email field to partition on here, so it keys on the authenticated user.
    [HttpPost("tickets/{ticketId:guid}/messages")]
    [ApplyEmailIpRateLimit]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] ReplyRequest request, CancellationToken ct)
    {
        SupportMessage message;
        try
        {
            message = await messages.PostUserReplyAsync(ticketId, request.Body, ct);
        }
        catch (SupportTicketNotFoundException)
        {
            // 404, not 422: a ticket you cannot reach is indistinguishable from one that
            // does not exist, matching GetOne / Close / thread GET (the IDOR-as-404 rule).
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // A reachable ticket whose status refuses the reply (Closed) — a real
            // transition refusal the UI can act on, so 422 with the reason.
            return Validation(ex);
        }

        // Notify the operator of the new user reply. The ticket carries the subject/priority
        // the notification quotes; GetThreadAsync scopes to the caller, so a foreign or
        // absent ticket would have already thrown out of PostUserReplyAsync above.
        var ticket = await messages.GetThreadAsync(ticketId, ct);
        if (ticket is not null)
            await notify.NotifyOperatorOfUserReplyAsync(ticket, FromDisplay(), message.Body, ct);

        return Ok(new MessageDto(
            message.Id, message.AuthorRole, message.Body, message.CreatedAt, []));
    }

    [HttpPost("tickets/{ticketId:guid}/close")]
    public async Task<IActionResult> Close(Guid ticketId, CancellationToken ct)
    {
        // 404 and 422 mean different things and must not be collapsed. A ticket you
        // cannot reach answers 404, matching GetOne above, so an id you do not own is
        // indistinguishable from one that does not exist — and the id never appears in a
        // response body. Already-closed is a real state-transition refusal on a ticket
        // you do own, so it is a 422 the UI can act on.
        return await tickets.CloseAsync(ticketId, ct) switch
        {
            CloseTicketResult.Closed => NoContent(),
            CloseTicketResult.NotFound => NotFound(),
            CloseTicketResult.AlreadyClosed => UnprocessableEntity(new
            {
                error = new
                {
                    code = "TICKET_ALREADY_CLOSED",
                    message = "This ticket is already closed. File a follow-up ticket to continue.",
                    details = Array.Empty<object>(),
                }
            }),
            _ => throw new InvalidOperationException("Unhandled CloseTicketResult."),
        };
    }

    // Attachments hang off a MESSAGE, not a ticket: the composite FK is (SupportMessageId,
    // UserId), and the service scopes the message to the caller so a foreign or absent
    // message is a 422 the same way. A Closed ticket's thread is frozen — the user cannot
    // post a new message to attach to — so the state-machine gate on the reply endpoint is
    // what keeps files off a closed conversation; this route does not re-check status.
    [HttpPost("messages/{messageId:guid}/attachments")]
    [RequestSizeLimit(11 * 1024 * 1024)] // 10 MB payload + multipart overhead
    public async Task<IActionResult> Upload(Guid messageId, IFormFile file, CancellationToken ct)
    {
        try
        {
            var saved = await attachments.UploadForSupportMessageAsync(messageId, file);
            return Ok(new AttachmentDto(
                saved.Id, saved.FileName, saved.ContentType, saved.FileSizeBytes, saved.UploadedAt));
        }
        catch (InvalidOperationException ex)
        {
            return Validation(ex);
        }
    }

    private static TicketThreadDto ToThreadDto(SupportTicket t) =>
        new(t.Id, t.Subject, t.Status, t.Priority, t.PrecedingTicketId, t.CreatedAt, t.UpdatedAt,
            [.. t.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new MessageDto(
                    m.Id, m.AuthorRole, m.Body, m.CreatedAt,
                    [.. m.Attachments.Select(a => new AttachmentDto(
                        a.Id, a.FileName, a.ContentType, a.FileSizeBytes, a.UploadedAt))]))]);

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

    // The operator notification names who filed the ticket. Prefer the display identity;
    // fall back to the user id when the principal has no name claim.
    private string FromDisplay() => User.Identity?.Name ?? currentUser.UserId.ToString();
}
