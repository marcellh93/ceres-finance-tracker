using FluentAssertions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Verifies the account balance derivation formula:
/// income transactions add to balance; expense transactions subtract.
/// Amount is always positive — direction comes from CategoryType.
/// </summary>
public class BalanceCalculationTests
{
    // Mirrors the formula in AccountService.GetBalanceAsync and ReportService.GetNetWorthAsync.
    private static decimal CalculateBalance(IEnumerable<Transaction> transactions) =>
        transactions.Sum(t =>
            t.Category.CategoryType.Name == "Income" ? t.Amount : -t.Amount);

    private static Transaction Income(decimal amount) => new()
    {
        Id     = Guid.NewGuid(),
        Amount = amount,
        Category = new Category
        {
            CategoryType = new CategoryType { Name = "Income" }
        }
    };

    private static Transaction Expense(decimal amount) => new()
    {
        Id     = Guid.NewGuid(),
        Amount = amount,
        Category = new Category
        {
            CategoryType = new CategoryType { Name = "Expense" }
        }
    };

    [Fact]
    public void EmptyTransactionList_ReturnsZero()
    {
        CalculateBalance([]).Should().Be(0m);
    }

    [Fact]
    public void SingleIncomeTransaction_ReturnsPositiveBalance()
    {
        var txns = new[] { Income(1000m) };
        CalculateBalance(txns).Should().Be(1000m);
    }

    [Fact]
    public void SingleExpenseTransaction_ReturnsNegativeBalance()
    {
        var txns = new[] { Expense(300m) };
        CalculateBalance(txns).Should().Be(-300m);
    }

    [Fact]
    public void MixedTransactions_ReturnsNetBalance()
    {
        var txns = new[]
        {
            Income(2000m),
            Expense(500m),
            Expense(200m),
            Income(300m)
        };
        // 2000 + 300 - 500 - 200 = 1600
        CalculateBalance(txns).Should().Be(1600m);
    }

    [Fact]
    public void ExpensesExceedIncome_ReturnsNegativeBalance()
    {
        var txns = new[]
        {
            Income(100m),
            Expense(400m)
        };
        CalculateBalance(txns).Should().Be(-300m);
    }

    [Fact]
    public void MultipleIncomeTransactions_SumCorrectly()
    {
        var txns = new[]
        {
            Income(1500m),
            Income(500m),
            Income(250.50m)
        };
        CalculateBalance(txns).Should().Be(2250.50m);
    }
}
