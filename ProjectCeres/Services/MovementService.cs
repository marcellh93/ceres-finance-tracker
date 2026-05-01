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
        int offset = 0,
        string? q = null,
        MovementType? type = null)
    {
        var transactions = type is null or MovementType.Transaction
            ? await QueryTransactions(accountId, from, to, q)
            : new List<MovementListItemViewModel>();
        var transfers = type is null or MovementType.Transfer
            ? await QueryTransfers(accountId, from, to, q)
            : new List<MovementListItemViewModel>();
        var payments = type is null or MovementType.LiabilityPayment
            ? await QueryLiabilityPayments(accountId, from, to, q)
            : new List<MovementListItemViewModel>();

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
        DateOnly? to = null,
        string? q = null,
        MovementType? type = null)
    {
        var t = type is null or MovementType.Transaction
            ? (await QueryTransactions(accountId, from, to, q)).Count : 0;
        var tr = type is null or MovementType.Transfer
            ? (await QueryTransfers(accountId, from, to, q)).Count : 0;
        var lp = type is null or MovementType.LiabilityPayment
            ? (await QueryLiabilityPayments(accountId, from, to, q)).Count : 0;
        return t + tr + lp;
    }

    public async Task<MovementType?> GetTypeAsync(Guid id)
    {
        if (await _db.Transactions.AnyAsync(t => t.Id == id))     return MovementType.Transaction;
        if (await _db.Transfers.AnyAsync(t => t.Id == id))        return MovementType.Transfer;
        if (await _db.LiabilityPayments.AnyAsync(p => p.Id == id)) return MovementType.LiabilityPayment;
        return null;
    }

    // -------------------------------------------------------------------------
    // Private query helpers
    // -------------------------------------------------------------------------

    private async Task<List<MovementListItemViewModel>> QueryTransactions(
        Guid? accountId, DateOnly? from, DateOnly? to, string? q = null)
    {
        var query = _db.Transactions
            .Include(t => t.Account).ThenInclude(a => a.Currency)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t =>
                EF.Functions.ILike(t.Description ?? "", $"%{q}%") ||
                EF.Functions.ILike(t.Category.Name, $"%{q}%"));

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
            CurrencySymbol   = t.Account.Currency.Symbol,
            CategoryId       = t.CategoryId,
            CategoryName     = t.Category.Name,
            CategoryTypeName = t.Category.CategoryType.Name
        }).ToListAsync();
    }

    private async Task<List<MovementListItemViewModel>> QueryTransfers(
        Guid? accountId, DateOnly? from, DateOnly? to, string? q = null)
    {
        var query = _db.Transfers
            .Include(t => t.SourceAccount).ThenInclude(a => a.Currency)
            .Include(t => t.DestAccount)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.SourceAccountId == accountId.Value || t.DestAccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(t => EF.Functions.ILike(t.Description ?? "", $"%{q}%"));

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
            CurrencySymbol      = t.SourceAccount.Currency.Symbol,
            DestAccountId       = t.DestAccountId,
            DestAccountName     = t.DestAccount.Name
        }).ToListAsync();
    }

    private async Task<List<MovementListItemViewModel>> QueryLiabilityPayments(
        Guid? accountId, DateOnly? from, DateOnly? to, string? q = null)
    {
        var query = _db.LiabilityPayments
            .Include(p => p.AssetAccount).ThenInclude(a => a.Currency)
            .Include(p => p.LiabilityAccount)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(p => p.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.Date <= to.Value);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => EF.Functions.ILike(p.Description ?? "", $"%{q}%"));

        return await query.Select(p => new MovementListItemViewModel
        {
            Id                   = p.Id,
            MovementType         = MovementType.LiabilityPayment,
            Date                 = p.Date,
            Amount               = p.Amount,
            CurrencySymbol       = p.AssetAccount.Currency.Symbol,
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
