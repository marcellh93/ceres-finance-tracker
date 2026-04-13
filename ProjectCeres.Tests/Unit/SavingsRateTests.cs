using FluentAssertions;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Verifies the savings rate formula used in ReportService.GetIncomeExpenseSummaryAsync:
///   savings rate = (income - expenses) / income   when income > 0
///   savings rate = 0                               when income == 0
///
/// A savings rate of 1.0 means all income was saved; 0 means all income was spent;
/// negative means expenses exceeded income.
/// </summary>
public class SavingsRateTests
{
    // Mirrors the formula in ReportService.GetIncomeExpenseSummaryAsync.
    private static decimal SavingsRate(decimal income, decimal expenses) =>
        income > 0 ? (income - expenses) / income : 0;

    [Fact]
    public void ZeroIncome_ReturnsSavingsRateOfZero()
    {
        SavingsRate(income: 0m, expenses: 0m).Should().Be(0m);
    }

    [Fact]
    public void ZeroIncome_WithExpenses_ReturnsSavingsRateOfZero()
    {
        // No income at all — savings rate is 0, not negative infinity.
        SavingsRate(income: 0m, expenses: 500m).Should().Be(0m);
    }

    [Fact]
    public void AllIncomeSaved_NoExpenses_ReturnsSavingsRateOfOne()
    {
        SavingsRate(income: 1000m, expenses: 0m).Should().Be(1.0m);
    }

    [Fact]
    public void NormalCase_ReturnsFractionalSavingsRate()
    {
        // 1000 income, 600 expenses → saved 400 → savings rate = 0.4
        SavingsRate(income: 1000m, expenses: 600m).Should().Be(0.4m);
    }

    [Fact]
    public void ExpensesEqualIncome_ReturnsSavingsRateOfZero()
    {
        SavingsRate(income: 800m, expenses: 800m).Should().Be(0m);
    }

    [Fact]
    public void ExpensesExceedIncome_ReturnsNegativeSavingsRate()
    {
        // 500 income, 1000 expenses → savings rate = (500 - 1000) / 500 = -1.0
        SavingsRate(income: 500m, expenses: 1000m).Should().Be(-1.0m);
    }

    [Fact]
    public void SavingsRateReflectsPrecision()
    {
        // 3000 income, 1000 expenses → savings rate = 2/3 ≈ 0.6667
        var rate = SavingsRate(income: 3000m, expenses: 1000m);
        rate.Should().BeApproximately(0.6667m, precision: 0.0001m);
    }
}
