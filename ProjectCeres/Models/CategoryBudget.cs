using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class CategoryBudget
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public int CurrencyId { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal LimitAmount { get; set; }
    public bool IsActive { get; set; }

    public Category Category { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
}
