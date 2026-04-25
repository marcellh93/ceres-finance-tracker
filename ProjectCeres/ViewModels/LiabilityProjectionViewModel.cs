namespace ProjectCeres.ViewModels;

public class LiabilityProjectionViewModel
{
    public int MonthsToPayoff { get; init; }
    public DateOnly PayoffDate { get; init; }
    public decimal TotalInterest { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal MonthlyPayment { get; init; }
}
