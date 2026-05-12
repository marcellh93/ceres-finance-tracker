using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Services.Reports;

public record BudgetVsActualRow(
    string CategoryName,
    string CurrencyCode,
    string CurrencySymbol,
    decimal LimitPerPeriod,
    decimal TotalLimit,
    decimal ActualSpend,
    decimal Variance);

public class BudgetVsActualReportGenerator(AppDbContext db, ISettingsService settingsService, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var settings = await settingsService.GetAsync();
        var periodCount = CountPeriodsInRange(from, to, settings.PeriodStartDay);

        var budgets = await db.CategoryBudgets
            .Owned(user)
            .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
            .Include(cb => cb.Category)
            .Include(cb => cb.Currency)
            .ToListAsync();

        var rows = new List<BudgetVsActualRow>();

        foreach (var budget in budgets)
        {
            var actual = await db.Transactions
                .Owned(user)
                .Where(t =>
                    t.CategoryId == budget.CategoryId &&
                    t.Account.CurrencyId == currencyId &&
                    t.Date >= from &&
                    t.Date <= to)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            var totalLimit = budget.LimitAmount * periodCount;

            rows.Add(new BudgetVsActualRow(
                budget.Category.Name,
                budget.Currency.Code,
                budget.Currency.Symbol,
                budget.LimitAmount,
                totalLimit,
                actual,
                totalLimit - actual));
        }

        return rows;
    }

    private static int CountPeriodsInRange(DateOnly from, DateOnly to, int startDay)
    {
        var (startYear, startMonth) = BudgetPeriod.GetCurrentPeriodMonth(from, startDay);
        var (endYear,   endMonth)   = BudgetPeriod.GetCurrentPeriodMonth(to,   startDay);
        return (endYear - startYear) * 12 + (endMonth - startMonth) + 1;
    }
}
