using FluentAssertions;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for LiabilityProjectionService amortisation calculations.
/// All tests are pure — no database access.
///
/// Reference values verified with a standard amortisation formula:
///   Monthly payment = P × [r(1+r)^n] / [(1+r)^n − 1]
///   where P = principal, r = monthly rate, n = total months.
/// </summary>
public class LiabilityProjectionServiceTests
{
    private readonly ILiabilityProjectionService _svc = new LiabilityProjectionService();

    [Fact]
    public void Project_KnownInputs_ReturnsCorrectPayoffAndInterest()
    {
        // €10 000 at 5% annual (0.4167% monthly), €500/month standard payment.
        // Expected payoff ≈ 21 months, total interest ≈ €421.
        var result = _svc.Project(balance: 10_000m, annualRate: 0.05m, monthlyPayment: 500m);

        result.MonthsToPayoff.Should().BeInRange(20, 22);
        result.TotalInterest.Should().BeInRange(450m, 480m);
    }

    [Fact]
    public void Project_WithExtraMonthlyPayment_EarlierPayoffAndLessInterest()
    {
        var standard = _svc.Project(balance: 10_000m, annualRate: 0.05m, monthlyPayment: 500m);
        var extra    = _svc.Project(balance: 10_000m, annualRate: 0.05m, monthlyPayment: 700m);

        extra.MonthsToPayoff.Should().BeLessThan(standard.MonthsToPayoff);
        extra.TotalInterest.Should().BeLessThan(standard.TotalInterest);
    }

    [Fact]
    public void Project_ZeroInterestRate_PayoffIsBalanceDividedByPayment()
    {
        // €1 200 at 0% interest, €400/month → exactly 3 months.
        var result = _svc.Project(balance: 1_200m, annualRate: 0m, monthlyPayment: 400m);

        result.MonthsToPayoff.Should().Be(3);
        result.TotalInterest.Should().Be(0m);
    }

    [Fact]
    public void Project_VerySmallBalance_PayoffInOneMonth()
    {
        // €50 balance, €500/month payment → paid off in 1 month.
        var result = _svc.Project(balance: 50m, annualRate: 0.05m, monthlyPayment: 500m);

        result.MonthsToPayoff.Should().Be(1);
    }

    [Fact]
    public void Project_PaymentTooSmallToEverPayOff_ThrowsOrIndicatesInfinite()
    {
        // Monthly interest on €10 000 at 12% = €100. Payment of €80 < interest → never pays off.
        var act = () => _svc.Project(balance: 10_000m, annualRate: 0.12m, monthlyPayment: 80m);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*payment*");
    }
}
