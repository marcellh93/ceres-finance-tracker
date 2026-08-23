namespace ProjectCeres.Models;

/// <summary>
/// Lifecycle of a support ticket. The service enforces that a user may only close
/// their own Open ticket and that closing is final — this enum is a plain settable
/// property and cannot enforce it alone. There is no reopen: continuing a
/// conversation means filing a follow-up ticket that references the closed one
/// (see <see cref="SupportTicket.PrecedingTicketId"/>), so every ticket keeps a
/// single, immutable lifecycle.
///
/// <para>
/// InProgress and Resolved have no user-facing path — they are the admin triage
/// states, reachable once the admin ticket list ships (roadmap § 12.5.2). Stored
/// as an int, so the ordinals are the database contract: do not reorder.
/// </para>
/// </summary>
public enum SupportTicketStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3,
}
