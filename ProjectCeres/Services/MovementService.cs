using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class MovementService : IMovementService
{
    private readonly AppDbContext _db;

    public MovementService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MovementListItemViewModel>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0)
    {
        var transactions = await QueryTransactions(accountId, from, to);
        var transfers    = await QueryTransfers(accountId, from, to);
        var payments     = await QueryLiabilityPayments(accountId, from, to);

        return transactions
            .Concat(transfers)
            .Concat(payments)
            .OrderByDescending(m => m.Date)
            .ThenByDescending(m => m.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var transactions = await QueryTransactions(accountId, from, to);
        var transfers    = await QueryTransfers(accountId, from, to);
        var payments     = await QueryLiabilityPayments(accountId, from, to);

        return transactions.Count + transfers.Count + payments.Count;
    }

    // -------------------------------------------------------------------------
    // Private query helpers
    // -------------------------------------------------------------------------

    private async Task<List<MovementListItemViewModel>> QueryTransactions(
        Guid? accountId, DateOnly? from, DateOnly? to)
    {
        var query = _db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        return await query.Select(t => new MovementListItemViewModel
        {
            Id           = t.Id,
            MovementType = MovementType.Transaction,
            Date         = t.Date,
            Amount       = t.Amount,
            Description  = t.Description,
            IsCleared    = t.IsCleared,
            CreatedAt    = t.CreatedAt,
            AccountId        = t.AccountId,
            AccountName      = t.Account.Name,
            CategoryId       = t.CategoryId,
            CategoryName     = t.Category.Name,
            CategoryTypeName = t.Category.CategoryType.Name
        }).ToListAsync();
    }

    private async Task<List<MovementListItemViewModel>> QueryTransfers(
        Guid? accountId, DateOnly? from, DateOnly? to)
    {
        var query = _db.Transfers
            .Include(t => t.SourceAccount)
            .Include(t => t.DestAccount)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.SourceAccountId == accountId.Value || t.DestAccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        return await query.Select(t => new MovementListItemViewModel
        {
            Id                  = t.Id,
            MovementType        = MovementType.Transfer,
            Date                = t.Date,
            Amount              = t.Amount,
            Description         = t.Description,
            IsCleared           = t.IsCleared,
            CreatedAt           = t.CreatedAt,
            SourceAccountId     = t.SourceAccountId,
            SourceAccountName   = t.SourceAccount.Name,
            DestAccountId       = t.DestAccountId,
            DestAccountName     = t.DestAccount.Name
        }).ToListAsync();
    }

    private async Task<List<MovementListItemViewModel>> QueryLiabilityPayments(
        Guid? accountId, DateOnly? from, DateOnly? to)
    {
        var query = _db.LiabilityPayments
            .Include(p => p.AssetAccount)
            .Include(p => p.LiabilityAccount)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(p => p.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.Date <= to.Value);

        return await query.Select(p => new MovementListItemViewModel
        {
            Id                   = p.Id,
            MovementType         = MovementType.LiabilityPayment,
            Date                 = p.Date,
            Amount               = p.Amount,
            Description          = p.Description,
            IsCleared            = p.IsCleared,
            CreatedAt            = p.CreatedAt,
            AssetAccountId       = p.AssetAccountId,
            AssetAccountName     = p.AssetAccount.Name,
            LiabilityAccountId   = p.LiabilityAccountId,
            LiabilityAccountName = p.LiabilityAccount.Name
        }).ToListAsync();
    }
}
