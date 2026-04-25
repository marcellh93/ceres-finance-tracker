using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransactionService(
    AppDbContext db,
    IAccountService accountService,
    ILiabilityPaymentService liabilityPaymentService,
    IFileAttachmentService attachmentService) : ITransactionService
{
    public async Task<IEnumerable<TransactionListItemViewModel>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0)
    {
        // --- Regular transactions ---
        var txQuery = db.Transactions
            .Where(t => !t.Category.IsSystem)
            .Include(t => t.Account)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .AsQueryable();

        if (accountId.HasValue)
            txQuery = txQuery.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            txQuery = txQuery.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            txQuery = txQuery.Where(t => t.Date <= to.Value);

        var transactions = await txQuery.ToListAsync();

        var txItems = transactions.Select(t => new TransactionListItemViewModel
        {
            Id               = t.Id,
            Date             = t.Date,
            CreatedAt        = t.CreatedAt,
            Amount           = t.Amount,
            Description      = t.Description,
            TransactionType  = "Regular",
            IsCleared        = t.IsCleared,
            AccountName      = t.Account.Name,
            CategoryName     = t.Category.Name,
            CategoryTypeName = t.Category.CategoryType.Name
        });

        // --- Liability payments ---
        var lpQuery = db.LiabilityPayments
            .Include(p => p.AssetAccount)
            .Include(p => p.LiabilityAccount)
            .AsQueryable();

        if (accountId.HasValue)
            lpQuery = lpQuery.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
        if (from.HasValue)
            lpQuery = lpQuery.Where(p => p.Date >= from.Value);
        if (to.HasValue)
            lpQuery = lpQuery.Where(p => p.Date <= to.Value);

        var payments = await lpQuery.ToListAsync();

        var lpItems = payments.Select(p => new TransactionListItemViewModel
        {
            Id                   = p.Id,
            Date                 = p.Date,
            CreatedAt            = p.CreatedAt,
            Amount               = p.Amount,
            Description          = p.Description,
            TransactionType      = "LiabilityPayment",
            AssetAccountName     = p.AssetAccount.Name,
            LiabilityAccountName = p.LiabilityAccount.Name
        });

        // --- Merge, sort, paginate ---
        return txItems
            .Concat(lpItems)
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountAsync(Guid? accountId = null, DateOnly? from = null, DateOnly? to = null)
    {
        var txQuery = db.Transactions
            .Where(t => !t.Category.IsSystem)
            .AsQueryable();

        if (accountId.HasValue)
            txQuery = txQuery.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            txQuery = txQuery.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            txQuery = txQuery.Where(t => t.Date <= to.Value);

        var lpQuery = db.LiabilityPayments.AsQueryable();

        if (accountId.HasValue)
            lpQuery = lpQuery.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
        if (from.HasValue)
            lpQuery = lpQuery.Where(p => p.Date >= from.Value);
        if (to.HasValue)
            lpQuery = lpQuery.Where(p => p.Date <= to.Value);

        return await txQuery.CountAsync() + await lpQuery.CountAsync();
    }

    public async Task<TransactionEditViewModel?> GetByIdForEditAsync(Guid id)
    {
        // Try regular transaction first
        var t = await db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .Include(t => t.Budget)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (t is not null)
        {
            return new TransactionEditViewModel
            {
                Id              = t.Id,
                TransactionType = "Regular",
                Date            = t.Date,
                Amount          = t.Amount,
                Description     = t.Description,
                AccountId       = t.AccountId,
                CategoryId      = t.CategoryId,
                BudgetId        = t.BudgetId,
                IsCleared       = t.IsCleared
            };
        }

        // Fall back to liability payment
        var p = await db.LiabilityPayments
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is not null)
        {
            return new TransactionEditViewModel
            {
                Id                 = p.Id,
                TransactionType    = "LiabilityPayment",
                Date               = p.Date,
                Amount             = p.Amount,
                Description        = p.Description,
                AccountId          = p.AssetAccountId,
                LiabilityAccountId = p.LiabilityAccountId
            };
        }

        return null;
    }

    public async Task<Guid> CreateAsync(TransactionCreateViewModel vm)
    {
        if (vm.TransactionType == "LiabilityPayment")
        {
            var payment = await liabilityPaymentService.CreateAsync(vm);
            return payment.Id;
        }

        if (vm.CategoryId is null)
            throw new InvalidOperationException("Please select a category.");

        await ValidateNotBeforeOpeningBalanceAsync(vm.AccountId!.Value, vm.Date);

        var transaction = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = vm.Date,
            Amount      = vm.Amount,
            Description = vm.Description,
            AccountId   = vm.AccountId!.Value,
            CategoryId  = vm.CategoryId!.Value,
            BudgetId    = vm.BudgetId,
            CreatedAt   = DateTime.UtcNow
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.Id;
    }

    public async Task UpdateAsync(TransactionEditViewModel vm)
    {
        if (vm.TransactionType == "LiabilityPayment")
        {
            await liabilityPaymentService.UpdateAsync(vm);
            return;
        }

        if (vm.CategoryId is null)
            throw new InvalidOperationException("Please select a category.");

        await ValidateNotBeforeOpeningBalanceAsync(vm.AccountId!.Value, vm.Date);

        var transaction = await db.Transactions.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Transaction {vm.Id} not found.");

        transaction.Date        = vm.Date;
        transaction.Amount      = vm.Amount;
        transaction.Description = vm.Description;
        transaction.AccountId   = vm.AccountId!.Value;
        transaction.CategoryId  = vm.CategoryId!.Value;
        transaction.BudgetId    = vm.BudgetId;
        transaction.IsCleared   = vm.IsCleared;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        // Try regular transaction first
        var transaction = await db.Transactions
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transaction is not null)
        {
            foreach (var attachment in transaction.Attachments.ToList())
                await attachmentService.DeleteAsync(attachment.Id);

            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();
            return;
        }

        // Fall back to liability payment
        await liabilityPaymentService.DeleteAsync(id);
    }

    public async Task MarkClearedAsync(Guid id, bool cleared)
    {
        var transaction = await db.Transactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");
        transaction.IsCleared = cleared;
        await db.SaveChangesAsync();
    }

    public async Task BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null)
    {
        var query = db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && !t.Category.IsSystem);

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);

        var transactions = await query.ToListAsync();
        foreach (var t in transactions)
            t.IsCleared = true;

        await db.SaveChangesAsync();
    }

    private async Task ValidateNotBeforeOpeningBalanceAsync(Guid accountId, DateOnly date)
    {
        var openingDate = await accountService.GetOpeningBalanceDateAsync(accountId);
        if (openingDate.HasValue && date < openingDate.Value)
            throw new InvalidOperationException(
                $"This transaction cannot be dated before the opening balance date ({openingDate.Value:dd/MM/yyyy}). " +
                $"To allow earlier dates, edit the account and move the opening balance date to {date:dd/MM/yyyy} or earlier.");
    }
}
