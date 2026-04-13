using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class Transaction
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public Guid AccountId { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? BudgetId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Budget? Budget { get; set; }
    public ICollection<TransactionAttachment> Attachments { get; set; } = new List<TransactionAttachment>();
}
