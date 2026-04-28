using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Services;

public class TransactionExportService(AppDbContext db) : ITransactionExportService
{
    public async Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
        Guid?     accountId = null,
        DateOnly? from      = null,
        DateOnly? to        = null)
    {
        var query = db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .Where(t => !t.Category.IsSystem)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        var transactions = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        return transactions.Select(t => new TransactionExportRow(
            t.Date,
            t.Account.Name,
            t.Category.Name,
            t.Category.CategoryType.Name,
            t.Description,
            t.Amount)).ToList();
    }
}
