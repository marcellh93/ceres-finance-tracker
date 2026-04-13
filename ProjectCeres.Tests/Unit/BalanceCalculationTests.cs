using FluentAssertions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Verifies the account balance derivation formula.
/// Amount is always positive — direction comes from CategoryType.
/// For asset accounts:     income adds, expense subtracts.
/// For liability accounts: expense adds (increases what you owe), income subtracts.
/// </summary>
public class BalanceCalculationTests
{
    // Mirrors the formula in AccountService.GetBalanceAsync and ReportService.GetNetWorthAsync.
    private static decimal CalculateBalance(IEnumerable<Transaction> transactions, bool isLiability) =>
        transactions.Sum(t =>
        {
            bool isIncome = t.Category.CategoryType.Name == "Income";
            bool addsToBalance = isLiability ? !isIncome : isIncome;
            return addsToBalance ? t.Amount : -t.Amount;
        });

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

    // -------------------------------------------------------------------------
    // Asset accounts
    // -------------------------------------------------------------------------

    [Fact]
    public void Asset_EmptyTransactionList_ReturnsZero()
    {
        CalculateBalance([], isLiability: false).Should().Be(0m);
    }

    [Fact]
    public void Asset_SingleIncomeTransaction_ReturnsPositiveBalance()
    {
        CalculateBalance([Income(1000m)], isLiability: false).Should().Be(1000m);
    }

    [Fact]
    public void Asset_SingleExpenseTransaction_ReturnsNegativeBalance()
    {
        CalculateBalance([Expense(300m)], isLiability: false).Should().Be(-300m);
    }

    [Fact]
    public void Asset_MixedTransactions_ReturnsNetBalance()
    {
        var txns = new[] { Income(2000m), Expense(500m), Expense(200m), Income(300m) };
        // 2000 + 300 - 500 - 200 = 1600
        CalculateBalance(txns, isLiability: false).Should().Be(1600m);
    }

    // -------------------------------------------------------------------------
    // Liability accounts (e.g. credit card)
    // -------------------------------------------------------------------------

    [Fact]
    public void Liability_EmptyTransactionList_ReturnsZero()
    {
        CalculateBalance([], isLiability: true).Should().Be(0m);
    }

    [Fact]
    public void Liability_SingleExpenseTransaction_ReturnsPositiveBalance()
    {
        // Charging an expense to a credit card increases what you owe.
        CalculateBalance([Expense(300m)], isLiability: true).Should().Be(300m);
    }

    [Fact]
    public void Liability_SingleIncomeTransaction_ReturnsNegativeBalance()
    {
        // An income-type transaction on a liability (e.g. a refund/credit) reduces what you owe.
        CalculateBalance([Income(100m)], isLiability: true).Should().Be(-100m);
    }

    [Fact]
    public void Liability_MultipleExpenses_SumCorrectly()
    {
        var txns = new[] { Expense(200m), Expense(150m), Expense(50m) };
        CalculateBalance(txns, isLiability: true).Should().Be(400m);
    }

    [Fact]
    public void Liability_ExpensesAndRefund_ReturnsNetOwed()
    {
        var txns = new[] { Expense(500m), Expense(200m), Income(100m) };
        // 500 + 200 - 100 = 600
        CalculateBalance(txns, isLiability: true).Should().Be(600m);
    }
}
