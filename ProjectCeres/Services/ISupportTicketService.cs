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

    /// <summary>
    /// The caller's own tickets, newest first, each carrying its message-thread rollup
    /// (<see cref="SupportTicketListItem.MessageCount"/> and
    /// <see cref="SupportTicketListItem.LastMessageAt"/>) so the list view need not fetch
    /// each thread. Projected in SQL — the messages themselves are never materialised.
    /// </summary>
    Task<IReadOnlyList<SupportTicketListItem>> ListOwnAsync(CancellationToken ct = default);

    /// <summary>One of the caller's own tickets, with its attachments. Null if it is not theirs.</summary>
    Task<SupportTicket?> GetOwnAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>
    /// Closes one of the caller's own open tickets. Closing is final — there is no reopen.
    ///
    /// Returns a result rather than throwing so the caller can tell the two failure modes
    /// apart: a ticket you cannot reach must answer 404 (indistinguishable from one that
    /// does not exist), while a ticket you own that is already closed is a genuine 422.
    /// A single exception type collapsed both into one status.
    /// </summary>
    Task<CloseTicketResult> CloseAsync(Guid ticketId, CancellationToken ct = default);
}

/// <summary>
/// A ticket for the list view, with its conversation rolled up. <paramref name="MessageCount"/>
/// is the number of messages in the thread (never zero — creation writes the first one);
/// <paramref name="LastMessageAt"/> is the newest message's timestamp, so the UI can sort or
/// badge on recency without loading the thread.
/// </summary>
public sealed record SupportTicketListItem(
    Guid Id,
    string Subject,
    SupportTicketStatus Status,
    SupportTicketPriority Priority,
    Guid? PrecedingTicketId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    DateTime LastMessageAt);

public enum CloseTicketResult
{
    Closed,

    /// <summary>No such ticket, or it belongs to someone else — the caller must not learn which.</summary>
    NotFound,

    /// <summary>The caller's own ticket, but already closed. Closing is final.</summary>
    AlreadyClosed,
}
