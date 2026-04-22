namespace ProjectCeres.Services.Reports;

public class ReportGeneratorFactory(
    NetWorthGenerator netWorth,
    IncomeExpenseGenerator incomeExpense,
    ExpenseBreakdownGenerator expenseBreakdown,
    TransactionHistoryGenerator transactionHistory)
{
    public IReportGenerator GetGenerator(ReportTypeKey type) => type switch
    {
        ReportTypeKey.NetWorth           => netWorth,
        ReportTypeKey.IncomeExpense      => incomeExpense,
        ReportTypeKey.ExpenseBreakdown   => expenseBreakdown,
        ReportTypeKey.TransactionHistory => transactionHistory,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
