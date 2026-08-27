using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// A support request raised by a user. Immutable once filed apart from
/// <see cref="Status"/>: users cannot edit or delete a ticket, because the
/// history is the record of what was reported and when.
/// </summary>
public sealed class SupportTicket : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string Subject { get; set; } = "";

    /// <summary>External reference for the ticket (e.g. an issue-tracker id), set by an agent. Optional.</summary>
    public string? ExternalRef { get; set; }

    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;
    public SupportTicketPriority Priority { get; set; } = SupportTicketPriority.Normal;

    /// <summary>
    /// The closed ticket this one continues, or null for a standalone ticket.
    /// Reopening does not exist; a follow-up is a new ticket carrying context from
    /// the old one, so each ticket keeps one lifecycle and the thread is a chain.
    /// The service enforces that the referenced ticket belongs to the same user and
    /// is closed — the FK alone cannot express either rule.
    /// </summary>
    public Guid? PrecedingTicketId { get; set; }
    public SupportTicket? PrecedingTicket { get; set; }

    /// <summary>The conversation — one row per message, oldest first.</summary>
    public ICollection<SupportMessage> Messages { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
