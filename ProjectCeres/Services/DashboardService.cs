using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Services;

public class DashboardService(AppDbContext db, ISettingsService settingsService) : IDashboardService
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;
        var currency   = settings.DefaultCurrency;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var mtdFrom = new DateOnly(today.Year, today.Month, 1);

        // MTD transactions for the default currency.
        var mtdTransactions = await db.Transactions
            .Where(t => t.Date >= mtdFrom && t.Date <= today && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var mtdIncome   = mtdTransactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var mtdExpenses = mtdTransactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);
        var savingsRate = mtdIncome > 0 ? (mtdIncome - mtdExpenses) / mtdIncome : 0;

        // Net worth across all currencies.
        var reportService = new ReportService(db);
        var netWorth = await reportService.GetNetWorthAsync();

        // Pending reminders: active recurring transactions whose NextDueDate <= today.
        var pendingCount = await db.RecurringTransactions
            .CountAsync(r => r.IsActive && r.NextDueDate <= today);

        return new DashboardData(
            NetWorth:              netWorth,
            MtdIncome:             mtdIncome,
            MtdExpenses:           mtdExpenses,
            SavingsRate:           savingsRate,
            PendingRemindersCount: pendingCount,
            CurrencyCode:          currency.Code,
            CurrencySymbol:        currency.Symbol);
    }
}
