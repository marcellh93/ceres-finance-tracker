using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// A support ticket could not be reached by the current user — absent, or owned by
/// someone else (the two are indistinguishable by design). Distinct from the generic
/// transition-refused case so the API can answer 404 for reachability and 422 for a
/// refused status change, keeping the IDOR-as-404 shape uniform across the surface.
/// Subclasses <see cref="InvalidOperationException"/> so existing catch-all sites keep working.
/// </summary>
public sealed class SupportTicketNotFoundException(Guid ticketId)
    : InvalidOperationException($"Support ticket {ticketId} not found.");

public interface ISupportMessageService
{
    /// <summary>
    /// Posts a reply from the current user on one of their own tickets, then advances the
    /// ticket's status via <see cref="SupportTicketStateMachine.ResolveUserReply"/>. Throws
    /// <see cref="SupportTicketNotFoundException"/> if the ticket cannot be reached (absent or
    /// not owned by the caller), or <see cref="InvalidOperationException"/> if the transition is
    /// refused (e.g. the ticket is Closed).
    /// </summary>
    Task<SupportMessage> PostUserReplyAsync(Guid ticketId, string body, CancellationToken ct = default);

    /// <summary>One of the caller's own tickets with its full message thread, oldest first. Null if not theirs.</summary>
    Task<SupportTicket?> GetThreadAsync(Guid ticketId, CancellationToken ct = default);
}
