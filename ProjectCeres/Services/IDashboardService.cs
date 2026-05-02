namespace ProjectCeres.Services;

public record DashboardData(
    IReadOnlyList<NetWorthEntry> NetWorth,
    decimal MtdIncome,
    decimal MtdExpenses,
    decimal SavingsRate,
    decimal? PriorPeriodIncome,
    decimal? PriorPeriodExpenses,
    decimal? PriorPeriodSavingsRate,
    int PendingRemindersCount,
    string CurrencyCode,
    string CurrencySymbol);

public record HealthSnapshotData(
    decimal? AvailableToday,
    decimal? SafeToSpend,
    decimal? ImminentBills,
    decimal? LaterBills,
    decimal? BudgetReserve,
    decimal? RunwayMonths,
    decimal? AvgMonthlyExpense,
    decimal? CurrentMonthIncome,
    decimal? RollingAverageIncome,
    decimal? IncomeDeltaPercent,
    decimal? BudgetBurnRate,
    decimal? BudgetSpentMtd,
    decimal? BudgetTotalLimit,
    string CurrencySymbol,
    string CurrencyCode);

public interface IDashboardService
{
    /// <summary>
    /// Returns live dashboard data scoped to the default currency from Settings.
    /// MTD = month-to-date (1st of current month through today).
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync();

    /// <summary>
    /// Returns a financial health snapshot scoped to the default currency.
    /// Includes spendable balance, runway, income delta, and budget burn rate.
    /// </summary>
    Task<HealthSnapshotData> GetHealthSnapshotAsync();
}
