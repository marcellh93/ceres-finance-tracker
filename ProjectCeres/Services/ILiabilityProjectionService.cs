using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ILiabilityProjectionService
{
    /// <summary>
    /// Projects payoff date, total interest, and total paid for an amortising loan.
    /// </summary>
    /// <param name="balance">Outstanding balance (positive).</param>
    /// <param name="annualRate">Annual interest rate as a decimal (e.g. 0.035 for 3.5%).</param>
    /// <param name="monthlyPayment">Fixed monthly payment amount.</param>
    /// <exception cref="InvalidOperationException">Thrown when the monthly payment does not exceed the first month's interest.</exception>
    LiabilityProjectionViewModel Project(decimal balance, decimal annualRate, decimal monthlyPayment);
}
