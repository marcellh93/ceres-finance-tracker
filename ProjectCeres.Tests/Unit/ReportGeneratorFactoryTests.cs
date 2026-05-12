using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Services.Reports;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Unit;

public class ReportGeneratorFactoryTests
{
    private static ReportGeneratorFactory BuildFactory()
    {
        var nw   = new NetWorthGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var ie   = new IncomeExpenseGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var eb   = new ExpenseBreakdownGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var th   = new TransactionHistoryGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var bva  = new BudgetVsActualReportGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var le   = new LargestExpensesReportGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var mcf  = new MonthlyCashFlowReportGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var nwot = new NetWorthOverTimeReportGenerator(null!, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
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
