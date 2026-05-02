using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Services.Reports;

public record MonthlyCashFlowRow(
    int Year,
    int Month,
    string CurrencyCode,
    string CurrencySymbol,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal Net);

public class MonthlyCashFlowReportGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(-6));
        var to         = parameters.To   ?? DateOnly.FromDateTime(DateTime.Today);

        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        var transactions = await db.Transactions
            .Owned(user)
            .Where(t =>
                t.Date >= from &&
                t.Date <= to &&
                t.Account.CurrencyId == currencyId &&
                !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var rows = transactions
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g =>
            {
                var income   = g.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
                var expenses = g.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);
                return new MonthlyCashFlowRow(g.Key.Year, g.Key.Month, currency.Code, currency.Symbol, income, expenses, income - expenses);
            })
            .OrderBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ToList();

        return rows;
    }
}
