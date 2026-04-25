using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Services.Reports;

public record BudgetVsActualRow(
    string CategoryName,
    string CurrencyCode,
    string CurrencySymbol,
    decimal LimitAmount,
    decimal ActualSpend,
    decimal Variance);

public class BudgetVsActualReportGenerator(AppDbContext db) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var budgets = await db.CategoryBudgets
            .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
            .Include(cb => cb.Category)
            .Include(cb => cb.Currency)
            .ToListAsync();

        var rows = new List<BudgetVsActualRow>();

        foreach (var budget in budgets)
        {
            var actual = await db.Transactions
                .Where(t =>
                    t.CategoryId == budget.CategoryId &&
                    t.Account.CurrencyId == currencyId &&
                    t.Date >= from &&
                    t.Date <= to)
                .SumAsync(t => (decimal?)t.Amount) ?? 0m;

            rows.Add(new BudgetVsActualRow(
                budget.Category.Name,
                budget.Currency.Code,
                budget.Currency.Symbol,
                budget.LimitAmount,
                actual,
                budget.LimitAmount - actual));
        }

        return rows;
    }
}
