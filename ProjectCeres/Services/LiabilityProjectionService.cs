using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class LiabilityProjectionService : ILiabilityProjectionService
{
    private const int MaxMonths = 1200; // 100 years — safety cap

    public LiabilityProjectionViewModel Project(decimal balance, decimal annualRate, decimal monthlyPayment)
    {
        if (balance <= 0)
            throw new ArgumentOutOfRangeException(nameof(balance), "Balance must be positive.");

        if (monthlyPayment <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyPayment), "Monthly payment must be positive.");

        decimal monthlyRate = annualRate / 12m;

        // First month interest — if payment doesn't cover it the loan can never be repaid.
        decimal firstInterest = balance * monthlyRate;
        if (monthlyPayment <= firstInterest)
            throw new InvalidOperationException(
                $"The monthly payment of {monthlyPayment:C} does not exceed the first month's interest of {firstInterest:C}. " +
                "Increase the payment amount so it can reduce the principal.");

        decimal remaining     = balance;
        decimal totalInterest = 0m;
        int     months        = 0;

        while (remaining > 0 && months < MaxMonths)
        {
            decimal interest  = remaining * monthlyRate;
            decimal principal = Math.Min(monthlyPayment - interest, remaining);

            totalInterest += interest;
            remaining     -= principal;
            months++;
        }

        return new LiabilityProjectionViewModel
        {
            MonthsToPayoff = months,
            PayoffDate     = DateOnly.FromDateTime(DateTime.Today.AddMonths(months)),
            TotalInterest  = Math.Round(totalInterest, 2),
            TotalPaid      = Math.Round(balance + totalInterest, 2),
            MonthlyPayment = monthlyPayment
        };
    }
}
