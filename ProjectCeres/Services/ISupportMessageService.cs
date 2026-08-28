using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ISupportMessageService
{
    /// <summary>
    /// Posts a reply from the current user on one of their own tickets, then advances the
    /// ticket's status via <see cref="SupportTicketStateMachine.ResolveUserReply"/>. Throws
    /// <see cref="InvalidOperationException"/> if the ticket cannot be reached (absent or not
    /// owned by the caller) or if the transition is refused (e.g. the ticket is Closed).
    /// </summary>
    Task<SupportMessage> PostUserReplyAsync(Guid ticketId, string body, CancellationToken ct = default);

    /// <summary>One of the caller's own tickets with its full message thread, oldest first. Null if not theirs.</summary>
    Task<SupportTicket?> GetThreadAsync(Guid ticketId, CancellationToken ct = default);
}
