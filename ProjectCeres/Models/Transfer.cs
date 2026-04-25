namespace ProjectCeres.Models;

public class Transfer : Movement
{
    public Guid SourceAccountId { get; set; }
    public Guid DestAccountId { get; set; }

    public Account SourceAccount { get; set; } = null!;
    public Account DestAccount { get; set; } = null!;
    public ICollection<TransferAttachment> Attachments { get; set; } = new List<TransferAttachment>();
}
