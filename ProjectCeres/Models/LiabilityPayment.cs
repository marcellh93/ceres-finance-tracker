namespace ProjectCeres.Models;

public class LiabilityPayment : Movement
{
    public Guid AssetAccountId { get; set; }
    public Guid LiabilityAccountId { get; set; }

    public Account AssetAccount { get; set; } = null!;
    public Account LiabilityAccount { get; set; } = null!;
}
