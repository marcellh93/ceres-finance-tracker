using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One message in a support ticket's conversation. IUserOwned and stamped with the TICKET
/// OWNER's id even for Agent messages, so the user reads the whole thread under their own RLS
/// scope; AuthorRole (not ownership) marks a message as the operator's.
/// </summary>
public sealed class SupportMessage : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SupportTicketId { get; set; }

    public SupportMessageAuthor AuthorRole { get; set; } = SupportMessageAuthor.User;
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public SupportTicket SupportTicket { get; set; } = null!;
    public ICollection<SupportTicketAttachment> Attachments { get; set; } = [];
}
