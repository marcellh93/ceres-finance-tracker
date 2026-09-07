using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Admin;

/// <summary>
/// The operator surface for support tickets: one endpoint that posts an agent reply and/or
/// sets a ticket's status on ANOTHER user's ticket. Stage 12.6.
///
/// This is the first cross-user WRITE under <c>Admin/</c>. It reaches the ticket through
/// <see cref="AdminDbContext"/> (Postgres role <c>ceres_admin</c>, BYPASSRLS) because the
/// ticket belongs to a different tenant; the read pairs that with <c>IgnoreQueryFilters()</c>
/// so the EF global filter does not re-scope it to the admin's own id. The message it writes
/// is stamped with the TICKET OWNER's id (taken from the loaded row), so the user reads the
/// operator's reply under their own RLS scope — see the load-bearing comment on the write.
/// </summary>
/// <remarks>
/// Gated by <see cref="RequireAdminAttribute"/> at the class level (live DB role check, not a
/// cookie claim). <see cref="RequiresAdminContextAttribute"/> marks the BYPASSRLS dependency so
/// every RLS bypass in the codebase is greppable by one attribute (AdminContextDisciplineTests).
/// </remarks>
[ApiController]
[Route("api/admin/support")]
[RequireAdmin]
[RequiresAdminContext]
public sealed class SupportAdminApiController(
    AdminDbContext adminDb,
    IAuditLogWriter auditLog,
    ISupportNotificationService notify,
    ICurrentUserAccessor currentUser,
    IOptions<EmailOptions> emailOptions,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// An operator reply and/or a status set. <c>Body</c> null/blank means status-only (no
    /// message row is written). <c>Status</c> is the chosen target status; the state machine
    /// decides whether the transition is legal. The body caps at 5000 to match the user reply
    /// surface — the operator is trusted, so this is defense-in-depth against an unbounded write.
    /// The attribute targets the PARAMETER (bare, not `[property:]`) — on a record primary
    /// constructor `[property:]` lands where model binding does not look and ASP.NET throws at
    /// request time; same rule the user SupportApiController records document.
    /// </summary>
    public sealed record OperatorMessageRequest(
        [StringLength(5000)] string? Body,
        SupportTicketStatus Status);

    /// <summary>One ticket in the admin triage list. Mirrors the user-facing rollup
    /// (bodies never materialised) plus the OWNER identity the operator needs to know
    /// who filed it. Stage 12.5.2.</summary>
    public sealed record AdminTicketListItemDto(
        Guid Id,
        string Subject,
        SupportTicketStatus Status,
        SupportTicketPriority Priority,
        Guid? PrecedingTicketId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int MessageCount,
        DateTime LastMessageAt,
        Guid OwnerUserId,
        string OwnerEmail);

    /// <summary>Offset-paginated envelope. First paginated list in the API — offset over
    /// cursor because triage wants jump-to-page + a total; see api-contract.md.</summary>
    public sealed record AdminTicketListResponse(
        IReadOnlyList<AdminTicketListItemDto> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>
    /// The triage list: every user's tickets, newest activity first, paginated. Cross-tenant by
    /// design — the IDOR-404 rule applies to a single-id fetch, not the list. Reads the BYPASSRLS
    /// adminDb with IgnoreQueryFilters() (legal under Admin/, ADR-0065) and joins AspNetUsers for
    /// the owner email. Bodies are never loaded — MessageCount/LastMessageAt are a SQL rollup.
    /// </summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<AdminTicketListResponse>> ListTickets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] SupportTicketStatus? status = null,
        [FromQuery] SupportTicketPriority? priority = null,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = adminDb.SupportTickets.IgnoreQueryFilters().AsNoTracking();
        if (status is { } s) query = query.Where(t => t.Status == s);
        if (priority is { } p) query = query.Where(t => t.Priority == p);

        var total = await query.CountAsync(ct);

        // Join to AspNetUsers for the owner email. adminDb is an IdentityDbContext subclass, so
        // Users is available; project Email only, never other Identity fields.
        var items = await query
            .OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new AdminTicketListItemDto(
                t.Id, t.Subject, t.Status, t.Priority, t.PrecedingTicketId,
                t.CreatedAt, t.UpdatedAt,
                t.Messages.Count,
                t.Messages.Max(m => (DateTime?)m.CreatedAt) ?? t.CreatedAt,
                t.UserId,
                adminDb.Users.Where(u => u.Id == t.UserId).Select(u => u.Email!).FirstOrDefault() ?? ""))
            .ToListAsync(ct);

        return Ok(new AdminTicketListResponse(items, page, pageSize, total));
    }

    public sealed record AdminAttachmentDto(
        Guid Id, string FileName, string ContentType, long FileSizeBytes, DateTime UploadedAt);

    public sealed record AdminMessageDto(
        Guid Id,
        SupportMessageAuthor AuthorRole,
        string Body,
        DateTime CreatedAt,
        IReadOnlyList<AdminAttachmentDto> Attachments);

    /// <summary>The full thread for one ticket, plus the owner identity. Stage 12.5.2.</summary>
    public sealed record AdminTicketThreadDto(
        Guid Id,
        string Subject,
        SupportTicketStatus Status,
        SupportTicketPriority Priority,
        Guid OwnerUserId,
        string OwnerEmail,
        IReadOnlyList<AdminMessageDto> Messages);

    /// <summary>
    /// One ticket's full thread. 404-not-403 on a miss (matches PostMessage + the user surface
    /// IDOR rule). Cross-tenant read via the BYPASSRLS adminDb + IgnoreQueryFilters().
    /// </summary>
    [HttpGet("tickets/{id:guid}")]
    public async Task<ActionResult<AdminTicketThreadDto>> GetTicket(Guid id, CancellationToken ct)
    {
        var ticket = await adminDb.SupportTickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(t => t.Messages).ThenInclude(m => m.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        var ownerEmail = await adminDb.Users
            .Where(u => u.Id == ticket.UserId).Select(u => u.Email!).FirstOrDefaultAsync(ct) ?? "";

        var messages = ticket.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AdminMessageDto(
                m.Id, m.AuthorRole, m.Body, m.CreatedAt,
                m.Attachments
                    .OrderBy(a => a.UploadedAt)
                    .Select(a => new AdminAttachmentDto(a.Id, a.FileName, a.ContentType, a.FileSizeBytes, a.UploadedAt))
                    .ToList()))
            .ToList();

        return Ok(new AdminTicketThreadDto(
            ticket.Id, ticket.Subject, ticket.Status, ticket.Priority,
            ticket.UserId, ownerEmail, messages));
    }

    // Sends the agent-reply / Solved email to the user, so it is a mail-sending action and
    // carries the same rate limit as the user endpoints — the invariant the spec (§ Notifications
    // carry-forward) and both reviews (security H3) pinned in ArchitectureTests' mailSendingActions.
    // The EmailByUser partition falls to the operator's id here (the body has no email field), so
    // the cap is per-operator; ApplyEmailIpRateLimit is the per-IP backstop.
    [HttpPost("tickets/{id:guid}/messages")]
    [ApplyEmailIpRateLimit]
    [EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]
    public async Task<IActionResult> PostMessage(Guid id, [FromBody] OperatorMessageRequest request, CancellationToken ct)
    {
        // Cross-tenant read: the ticket belongs to another user, so IgnoreQueryFilters() is
        // REQUIRED — without it the global per-user filter scopes the read to the admin's own
        // id (currentUser) and returns zero rows. 404-not-403 on a miss: an id the operator
        // cannot resolve is indistinguishable from one that does not exist (IDOR rule, matching
        // the user surface). currentUser is injected only to make that scoping explicit here.
        _ = currentUser;
        var ticket = await adminDb.SupportTickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null) return NotFound();

        // Validate the transition BEFORE any write. Closed is terminal — the state machine
        // refuses it, and a refused transition is a 422 (Validation envelope), never a write.
        var result = SupportTicketStateMachine.ResolveOperatorAction(ticket.Status, request.Status);
        if (!result.Allowed) return Validation(result.Reason);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var body = request.Body?.Trim();
        var replyPosted = !string.IsNullOrEmpty(body);
        if (replyPosted)
        {
            adminDb.SupportMessages.Add(new SupportMessage
            {
                // LOAD-BEARING: stamp the TICKET OWNER's id explicitly, taken from the loaded
                // ticket row — never the admin, never a request field. Left unset, the
                // UserOwnershipInterceptor (UserOwnershipInterceptor.cs:38-41) stamps the CURRENT
                // user — the admin, on an operator request — and the message would land in the
                // admin's tenant: invisible to the user and unattachable to the owner's ticket
                // via the composite (SupportTicketId, UserId) FK. A request-supplied owner would
                // be exactly the cross-tenant write the composite FK exists to stop, and under
                // BYPASSRLS the WITH CHECK policy will not catch it.
                UserId = ticket.UserId,
                SupportTicketId = ticket.Id,
                AuthorRole = SupportMessageAuthor.Agent,
                Body = body!,
                CreatedAt = now,
            });
        }

        ticket.Status = result.NewStatus;
        ticket.UpdatedAt = now;
        await adminDb.SaveChangesAsync(ct);

        // Interim audit (spec § Operator surface step 6): the full append-only AdminAuditLog is
        // documented in models.md but deferred with Stage 13 (roadmap § 15.6 audit note). Owner-
        // stamped AuditLog stands in — RecordAsync writes the row owned by the ticket owner.
        await auditLog.RecordAsync(ticket.UserId, AuditLogAction.SupportMessageByAgent,
            nameof(SupportTicket), ticket.Id, ct);

        // Notify the user (best-effort; the service logs and swallows a mail failure). The
        // support-thread URL base is CONFIGURED (Email:PublicBaseUrl), never taken from the
        // request — an operator must not be able to point the user's link at an arbitrary host.
        // Falls back to the incoming request origin when unset (dev / tests).
        var supportUrlBase = SupportUrlBase();
        if (replyPosted)
            await notify.NotifyUserOfAgentReplyAsync(ticket, body!, supportUrlBase, ct);
        if (result.NewStatus == SupportTicketStatus.Solved)
            await notify.NotifyUserSolvedAsync(ticket, supportUrlBase, ct);

        return NoContent();
    }

    // Configured origin wins; otherwise the incoming request's scheme+host, matching the
    // established link-building pattern (AuthController, EmailVerificationController). Either
    // way the value is server-controlled — never a request body field.
    private string SupportUrlBase()
    {
        var configured = emailOptions.Value.PublicBaseUrl;
        return string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}"
            : configured;
    }

    private IActionResult Validation(string? message) =>
        UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = message ?? "The requested status transition is not allowed.",
                details = Array.Empty<object>(),
            }
        });
}
