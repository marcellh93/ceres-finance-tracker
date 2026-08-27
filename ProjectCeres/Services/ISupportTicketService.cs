using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ISupportTicketService
{
    /// <summary>
    /// Files a new ticket for the current user, always as <see cref="SupportTicketStatus.Open"/>.
    /// Pass <paramref name="precedingTicketId"/> to continue a closed ticket as a follow-up.
    /// </summary>
    Task<SupportTicket> CreateAsync(
        string subject,
        string message,
        SupportTicketPriority priority,
        Guid? precedingTicketId = null,
        CancellationToken ct = default);

    /// <summary>The caller's own tickets, newest first.</summary>
    Task<IReadOnlyList<SupportTicket>> ListOwnAsync(CancellationToken ct = default);

    /// <summary>One of the caller's own tickets, with its attachments. Null if it is not theirs.</summary>
    Task<SupportTicket?> GetOwnAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>Closes one of the caller's open tickets. Closing is final — there is no reopen.</summary>
    Task CloseAsync(Guid ticketId, CancellationToken ct = default);
}
