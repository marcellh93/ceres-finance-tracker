using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransactionService(AppDbContext db, IAccountService accountService) : ITransactionService
{
    public async Task<IEnumerable<Transaction>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0)
    {
        var query = db.Transactions
            .Where(t => !t.Category.IsSystem)   // opening balance is system-managed, not shown here
            .Include(t => t.Account)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .Include(t => t.Attachments)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        return await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> CountAsync(Guid? accountId = null, DateOnly? from = null, DateOnly? to = null)
    {
        var query = db.Transactions
            .Where(t => !t.Category.IsSystem)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (from.HasValue)
            query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.Date <= to.Value);

        return await query.CountAsync();
    }

    public async Task<Transaction?> GetByIdAsync(Guid id) =>
        await db.Transactions
            .Include(t => t.Account)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .Include(t => t.Budget)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Transaction> CreateAsync(TransactionCreateViewModel vm)
    {
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
        return transaction;
    }

    public async Task UpdateAsync(TransactionEditViewModel vm)
    {
        await ValidateNotBeforeOpeningBalanceAsync(vm.AccountId!.Value, vm.Date);

        var transaction = await db.Transactions.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Transaction {vm.Id} not found.");

        transaction.Date        = vm.Date;
        transaction.Amount      = vm.Amount;
        transaction.Description = vm.Description;
        transaction.AccountId   = vm.AccountId!.Value;
        transaction.CategoryId  = vm.CategoryId!.Value;
        transaction.BudgetId    = vm.BudgetId;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var transaction = await db.Transactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Transaction {id} not found.");

        db.Transactions.Remove(transaction);
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
