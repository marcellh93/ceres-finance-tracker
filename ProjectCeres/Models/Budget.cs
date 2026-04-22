using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class Budget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [Column(TypeName = "decimal(18,2)")]
    public decimal TargetAmount { get; set; }
    public int CurrencyId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string GoalType { get; set; } = "Spending";
    public Guid? LinkedAccountId { get; set; }

    public Currency Currency { get; set; } = null!;
    public Account? LinkedAccount { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
