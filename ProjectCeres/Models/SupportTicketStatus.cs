namespace ProjectCeres.Models;

/// <summary>
/// Lifecycle of a support ticket's conversation. The service enforces which
/// transitions are legal for a given actor — this enum is a plain settable
/// property and cannot enforce that alone.
///
/// <para>
/// Open: filed, awaiting first agent response. Pending: waiting on the user to
/// reply. OnHold: blocked on something other than the user (e.g. escalation) —
/// admin-only state, no user-facing path yet. Solved: the agent believes the
/// issue is resolved; still reopenable by a user reply. Closed: terminal — no
/// reopen; continuing the conversation means filing a follow-up ticket that
/// references the closed one (see <see cref="SupportTicket.PrecedingTicketId"/>).
/// Stored as an int, so the ordinals are the database contract: do not reorder.
/// </para>
/// </summary>
public enum SupportTicketStatus
{
    Open = 0,
    Pending = 1,
    OnHold = 2,
    Solved = 3,
    Closed = 4,
}
