namespace ProjectCeres.Models;

public class BudgetProgressResult
{
    public decimal AmountProgress { get; init; }
    public decimal TargetAmount { get; init; }
    public decimal Remaining => Math.Max(0m, TargetAmount - AmountProgress);
    public decimal PercentUsed => TargetAmount == 0m
        ? 0m
        : Math.Round(AmountProgress / TargetAmount * 100m, 2);
}
