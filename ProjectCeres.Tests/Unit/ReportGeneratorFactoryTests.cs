using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Unit;

public class ReportGeneratorFactoryTests
{
    private static ReportGeneratorFactory BuildFactory()
    {
        var user = new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"));
        var nw   = new NetWorthGenerator(null!, user);
        var ie   = new IncomeExpenseGenerator(null!, user);
        var eb   = new ExpenseBreakdownGenerator(null!, user);
        var th   = new TransactionHistoryGenerator(null!, user);
        var bva  = new BudgetVsActualReportGenerator(null!, new SettingsService(null!, user), user);
        var le   = new LargestExpensesReportGenerator(null!, user);
        var mcf  = new MonthlyCashFlowReportGenerator(null!, user);
        var nwot = new NetWorthOverTimeReportGenerator(null!, user);
        return new ReportGeneratorFactory(nw, ie, eb, th, bva, le, mcf, nwot);
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

    [Fact]
    public void GetGenerator_BudgetVsActual_ReturnsBudgetVsActualReportGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.BudgetVsActual).Should().BeOfType<BudgetVsActualReportGenerator>();

    [Fact]
    public void GetGenerator_LargestExpenses_ReturnsLargestExpensesReportGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.LargestExpenses).Should().BeOfType<LargestExpensesReportGenerator>();

    [Fact]
    public void GetGenerator_MonthlyCashFlow_ReturnsMonthlyCashFlowReportGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.MonthlyCashFlow).Should().BeOfType<MonthlyCashFlowReportGenerator>();

    [Fact]
    public void GetGenerator_NetWorthOverTime_ReturnsNetWorthOverTimeReportGenerator()
        => BuildFactory().GetGenerator(ReportTypeKey.NetWorthOverTime).Should().BeOfType<NetWorthOverTimeReportGenerator>();
}
