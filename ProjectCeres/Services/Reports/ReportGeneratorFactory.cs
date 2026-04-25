namespace ProjectCeres.Services.Reports;

public class ReportGeneratorFactory(
    NetWorthGenerator netWorth,
    IncomeExpenseGenerator incomeExpense,
    ExpenseBreakdownGenerator expenseBreakdown,
    TransactionHistoryGenerator transactionHistory,
    BudgetVsActualReportGenerator budgetVsActual,
    LargestExpensesReportGenerator largestExpenses,
    MonthlyCashFlowReportGenerator monthlyCashFlow,
    NetWorthOverTimeReportGenerator netWorthOverTime)
{
    public IReportGenerator GetGenerator(ReportTypeKey type) => type switch
    {
        ReportTypeKey.NetWorth           => netWorth,
        ReportTypeKey.IncomeExpense      => incomeExpense,
        ReportTypeKey.ExpenseBreakdown   => expenseBreakdown,
        ReportTypeKey.TransactionHistory => transactionHistory,
        ReportTypeKey.BudgetVsActual     => budgetVsActual,
        ReportTypeKey.LargestExpenses    => largestExpenses,
        ReportTypeKey.MonthlyCashFlow    => monthlyCashFlow,
        ReportTypeKey.NetWorthOverTime   => netWorthOverTime,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
