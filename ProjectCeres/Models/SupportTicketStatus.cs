namespace ProjectCeres.Models;

/// <summary>
/// Lifecycle of a support ticket. A ticket moves forward only: a user may close
/// their own Open ticket, and closing is final. There is no reopen — continuing a
/// conversation means filing a follow-up ticket that references the closed one
/// (see <see cref="SupportTicket.PrecedingTicketId"/>), so every ticket keeps a
/// single, immutable lifecycle.
/// </summary>
public enum SupportTicketStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3,
}
