using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class LiabilityPayment
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public Guid AssetAccountId { get; set; }
    public Guid LiabilityAccountId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    public Account AssetAccount { get; set; } = null!;
    public Account LiabilityAccount { get; set; } = null!;
}
