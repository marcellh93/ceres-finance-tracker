namespace ProjectCeres.Services;

public record DashboardData(
    IReadOnlyList<NetWorthEntry> NetWorth,
    decimal MtdIncome,
    decimal MtdExpenses,
    decimal SavingsRate,
    int PendingRemindersCount,
    string CurrencyCode,
    string CurrencySymbol);

public interface IDashboardService
{
    /// <summary>
    /// Returns live dashboard data scoped to the default currency from Settings.
    /// MTD = month-to-date (1st of current month through today).
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync();
}
