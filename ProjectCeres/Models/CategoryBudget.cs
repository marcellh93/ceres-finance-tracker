using System.ComponentModel.DataAnnotations.Schema;
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class CategoryBudget : IUserOwned
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public int CurrencyId { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal LimitAmount { get; set; }
    public bool IsActive { get; set; }
    public Guid UserId { get; set; }

    public Category Category { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
}
