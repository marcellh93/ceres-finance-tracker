using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Services.Reports;

public class IncomeExpenseGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        var transactions = await db.Transactions
            .Owned(user)
            .Where(t => t.Date >= from && t.Date <= to && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var income   = transactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var expenses = transactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);
        var savings  = income > 0 ? (income - expenses) / income : 0;

        return new IncomeExpenseSummary(currency.Code, currency.Symbol, income, expenses, savings);
    }
}
