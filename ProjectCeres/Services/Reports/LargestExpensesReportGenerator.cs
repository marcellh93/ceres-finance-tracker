using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Services.Reports;

public record LargestExpenseRow(
    DateOnly Date,
    string Description,
    string CategoryName,
    string AccountName,
    string CurrencySymbol,
    decimal Amount);

public class LargestExpensesReportGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var rows = await db.Transactions
            .Owned(user)
            .Where(t =>
                t.Date >= from &&
                t.Date <= to &&
                t.Account.CurrencyId == currencyId &&
                !t.Category.IsSystem &&
                t.Category.CategoryType.Name == "Expense")
            .Include(t => t.Account).ThenInclude(a => a.Currency)
            .Include(t => t.Category)
            .OrderByDescending(t => t.Amount)
            .Take(parameters.Limit)
            .Select(t => new LargestExpenseRow(
                t.Date,
                t.Description ?? string.Empty,
                t.Category.Name,
                t.Account.Name,
                t.Account.Currency.Symbol,
                t.Amount))
            .ToListAsync();

        return rows;
    }
}
