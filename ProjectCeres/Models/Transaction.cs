namespace ProjectCeres.Models;

public class Transaction : Movement
{
    public Guid AccountId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? BudgetId { get; set; }

    public bool NeedsReview { get; set; }

    public Account Account { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Budget? Budget { get; set; }
    public ICollection<TransactionAttachment> Attachments { get; set; } = new List<TransactionAttachment>();
}
