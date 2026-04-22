using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class Transfer
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid DestAccountId { get; set; }
    public string? Description { get; set; }
    public bool IsCleared { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account SourceAccount { get; set; } = null!;
    public Account DestAccount { get; set; } = null!;
    public ICollection<TransferAttachment> Attachments { get; set; } = new List<TransferAttachment>();
}
