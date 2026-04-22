using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Services.Reports;

public class ExpenseBreakdownGenerator(AppDbContext db) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        var transactions = await db.Transactions
            .Where(t =>
                t.Date >= from &&
                t.Date <= to &&
                t.Account.CurrencyId == currencyId &&
                t.Category.CategoryType.Name == "Expense")
            .Include(t => t.Category)
            .ToListAsync();

        var categories = transactions
            .GroupBy(t => new { t.Category.Name, t.Category.LifestyleTag })
            .Select(g => new CategoryExpense(g.Key.Name, g.Key.LifestyleTag, g.Sum(t => t.Amount)))
            .OrderByDescending(c => c.Total)
            .ToList();

        return new ExpenseBreakdown(currency.Code, currency.Symbol, categories);
    }
}
