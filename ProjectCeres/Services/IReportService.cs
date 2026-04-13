using ProjectCeres.Models;

namespace ProjectCeres.Services;

// ---------------------------------------------------------------------------
// Result types
// ---------------------------------------------------------------------------

public record NetWorthEntry(string CurrencyCode, string CurrencySymbol, decimal Assets, decimal Liabilities, decimal NetWorth);

public record IncomeExpenseSummary(
    string CurrencyCode,
    string CurrencySymbol,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal SavingsRate);

public record CategoryExpense(string CategoryName, string? LifestyleTag, decimal Total);

public record ExpenseBreakdown(string CurrencyCode, string CurrencySymbol, IReadOnlyList<CategoryExpense> Categories);

// ---------------------------------------------------------------------------
// Interface
// ---------------------------------------------------------------------------

public interface IReportService
{
    /// <summary>Net worth per currency: SUM(asset balances) − SUM(liability balances).</summary>
    Task<IReadOnlyList<NetWorthEntry>> GetNetWorthAsync();

    /// <summary>Income and expense totals for the given period, filtered by currency.</summary>
    Task<IncomeExpenseSummary> GetIncomeExpenseSummaryAsync(int currencyId, DateOnly from, DateOnly to);

    /// <summary>Expense totals grouped by category for the given period.</summary>
    Task<ExpenseBreakdown> GetExpenseBreakdownAsync(int currencyId, DateOnly from, DateOnly to);

    /// <summary>Filtered, paginated transaction history.</summary>
    Task<IReadOnlyList<Transaction>> GetTransactionHistoryAsync(
        int currencyId,
        DateOnly from,
        DateOnly to,
        Guid? accountId = null,
        Guid? categoryId = null,
        int limit = 50,
        int offset = 0);
}
