using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services.Reports;

public class TransactionHistoryGenerator(AppDbContext db, ICurrentUserAccessor user) : IReportGenerator
{
    public async Task<object> GenerateAsync(ReportParameters parameters)
    {
        var currencyId = parameters.CurrencyId ?? throw new ArgumentException("CurrencyId is required.");
        var from       = parameters.From        ?? throw new ArgumentException("From is required.");
        var to         = parameters.To          ?? throw new ArgumentException("To is required.");

        var query = db.Transactions
            .Owned(user)
            .Where(t => t.Date >= from && t.Date <= to && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Account)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .AsQueryable();

        if (parameters.AccountId.HasValue)
            query = query.Where(t => t.AccountId == parameters.AccountId.Value);
        if (parameters.CategoryId.HasValue)
            query = query.Where(t => t.CategoryId == parameters.CategoryId.Value);

        var results = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(parameters.Offset)
            .Take(parameters.Limit)
            .ToListAsync();

        return results;
    }
}
