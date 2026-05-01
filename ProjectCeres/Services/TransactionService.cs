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
            .Include(t => t.Account).ThenInclude(a => a.Currency)
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
            TransactionType  = TransactionTypes.Regular,
            IsCleared        = t.IsCleared,
            NeedsReview      = t.NeedsReview,
            AccountName      = t.Account.Name,
            CurrencySymbol   = t.Account.Currency.Symbol,
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
            TransactionType      = TransactionTypes.LiabilityPayment,
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
                TransactionType = TransactionTypes.Regular,
                Date            = t.Date,
                Amount          = t.Amount,
                Description     = t.Description,
                AccountId       = t.AccountId,
                CategoryId      = t.CategoryId,
                BudgetId        = t.BudgetId,
                IsCleared       = t.IsCleared,
                NeedsReview     = t.NeedsReview
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
                TransactionType    = TransactionTypes.LiabilityPayment,
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
        if (vm.TransactionType == TransactionTypes.LiabilityPayment)
        {
            var payment = await liabilityPaymentService.CreateAsync(vm);
            return payment.Id;
        }

        if (vm.CategoryId is null)
            throw new InvalidOperationException("Please select a category.");

        await ValidateNotBeforeOpeningBalanceAsync(vm.AccountId!.Value, vm.Date);
        await ValidateBudgetCurrencyAsync(vm.BudgetId, vm.AccountId!.Value);

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
        if (vm.TransactionType == TransactionTypes.LiabilityPayment)
        {
            await liabilityPaymentService.UpdateAsync(vm);
            return;
        }

        if (vm.CategoryId is null)
            throw new InvalidOperationException("Please select a category.");

        await ValidateNotBeforeOpeningBalanceAsync(vm.AccountId!.Value, vm.Date);
        await ValidateBudgetCurrencyAsync(vm.BudgetId, vm.AccountId!.Value);

        var transaction = await db.Transactions.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Transaction {vm.Id} not found.");

        transaction.Date        = vm.Date;
        transaction.Amount      = vm.Amount;
        transaction.Description = vm.Description;
        transaction.AccountId   = vm.AccountId!.Value;
        transaction.CategoryId  = vm.CategoryId!.Value;
        transaction.BudgetId    = vm.BudgetId;
        transaction.IsCleared   = vm.IsCleared;
        transaction.NeedsReview = vm.NeedsReview;
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

    public async Task MarkNeedsReviewAsync(Guid id, bool needsReview)
    {
        var transaction = await db.Transactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");
        transaction.NeedsReview = needsReview;
        await db.SaveChangesAsync();
    }

    public async Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null, string? currency = null)
    {
        var query = db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && !t.Category.IsSystem && !t.IsCleared);

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(t => t.Account.Currency.Code == currency);

        var rowsAffected = await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.IsCleared, true));
        return rowsAffected;
    }

    private async Task ValidateNotBeforeOpeningBalanceAsync(Guid accountId, DateOnly date)
    {
        var openingDate = await accountService.GetOpeningBalanceDateAsync(accountId);
        if (openingDate.HasValue && date < openingDate.Value)
            throw new InvalidOperationException(
                $"This transaction cannot be dated before the opening balance date ({openingDate.Value:dd/MM/yyyy}). " +
                $"To allow earlier dates, edit the account and move the opening balance date to {date:dd/MM/yyyy} or earlier.");
    }

    private async Task ValidateBudgetCurrencyAsync(Guid? budgetId, Guid accountId)
    {
        if (budgetId is null) return;

        var budget  = await db.Budgets.FindAsync(budgetId.Value);
        var account = await db.Accounts.FindAsync(accountId);
        if (budget is null || account is null) return;

        if (budget.CurrencyId != account.CurrencyId)
            throw new InvalidOperationException(
                "The selected goal budget uses a different currency than the transaction account. " +
                "Select a budget that matches the account currency, or leave it blank.");
    }
}
