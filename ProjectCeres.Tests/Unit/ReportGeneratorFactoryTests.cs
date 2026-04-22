using FluentAssertions;
using ProjectCeres.Services.Reports;

namespace ProjectCeres.Tests.Unit;

public class ReportGeneratorFactoryTests
{
    private static ReportGeneratorFactory BuildFactory()
    {
        var nw  = new NetWorthGenerator(null!);
        var ie  = new IncomeExpenseGenerator(null!);
        var eb  = new ExpenseBreakdownGenerator(null!);
        var th  = new TransactionHistoryGenerator(null!);
        return new ReportGeneratorFactory(nw, ie, eb, th);
    }

    [Fact]
    public void GetGenerator_NetWorth_ReturnsNetWorthGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.NetWorth).Should().BeOfType<NetWorthGenerator>();

    [Fact]
    public void GetGenerator_IncomeExpense_ReturnsIncomeExpenseGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.IncomeExpense).Should().BeOfType<IncomeExpenseGenerator>();

    [Fact]
    public void GetGenerator_ExpenseBreakdown_ReturnsExpenseBreakdownGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.ExpenseBreakdown).Should().BeOfType<ExpenseBreakdownGenerator>();

    [Fact]
    public void GetGenerator_TransactionHistory_ReturnsTransactionHistoryGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.TransactionHistory).Should().BeOfType<TransactionHistoryGenerator>();
}
