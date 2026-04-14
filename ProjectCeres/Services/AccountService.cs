using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class AccountService(AppDbContext db) : IAccountService
{
    // Opening Balance category is seeded with this known Guid (see AppDbContext seed data).
    private static readonly Guid OpeningBalanceCategoryId = new("20000000-0000-0000-0000-000000000001");

    public async Task<IEnumerable<Account>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(a => a.IsActive);

        return await query.OrderBy(a => a.Name).ToListAsync();
    }

    public async Task<Account?> GetByIdAsync(Guid id) =>
        await db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .FirstOrDefaultAsync(a => a.Id == id);

    public async Task<Account> CreateAsync(AccountCreateViewModel vm)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = vm.Name,
            AccountTypeId = vm.AccountTypeId!.Value,
            CurrencyId    = vm.CurrencyId!.Value,
            Description   = vm.Description,
            IsActive      = true
        };

        db.Accounts.Add(account);

        if (vm.OpeningBalance != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                Date        = vm.OpeningBalanceDate,
                Amount      = Math.Abs(vm.OpeningBalance),
                Description = "Opening Balance",
                AccountId   = account.Id,
                CategoryId  = OpeningBalanceCategoryId,
                CreatedAt   = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAsync(AccountEditViewModel vm)
    {
        var account = await db.Accounts.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Account {vm.Id} not found.");

        account.Name        = vm.Name;
        account.Description = vm.Description;

        // Manage the opening balance transaction (system-managed, not user-editable directly).
        var existing = await db.Transactions
            .FirstOrDefaultAsync(t => t.AccountId == vm.Id && t.CategoryId == OpeningBalanceCategoryId);

        if (existing is not null)
        {
            if (vm.OpeningBalance == 0)
                db.Transactions.Remove(existing);
            else
            {
                existing.Amount = Math.Abs(vm.OpeningBalance);
                existing.Date   = vm.OpeningBalanceDate;
            }
        }
        else if (vm.OpeningBalance != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                Date        = vm.OpeningBalanceDate,
                Amount      = Math.Abs(vm.OpeningBalance),
                Description = "Opening Balance",
                AccountId   = vm.Id,
                CategoryId  = OpeningBalanceCategoryId,
                CreatedAt   = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<decimal> GetOpeningBalanceAsync(Guid id)
    {
        var t = await db.Transactions
            .FirstOrDefaultAsync(t => t.AccountId == id && t.CategoryId == OpeningBalanceCategoryId);
        return t?.Amount ?? 0;
    }

    public async Task<DateOnly?> GetOpeningBalanceDateAsync(Guid id)
    {
        var t = await db.Transactions
            .FirstOrDefaultAsync(t => t.AccountId == id && t.CategoryId == OpeningBalanceCategoryId);
        return t?.Date;
    }

    public async Task DeactivateAsync(Guid id)
    {
        var account = await db.Accounts.FindAsync(id)
            ?? throw new InvalidOperationException($"Account {id} not found.");

        account.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<decimal> GetBalanceAsync(Guid id)
    {
        var account = await db.Accounts
            .Include(a => a.AccountType)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (account is null) return 0;

        var transactions = await db.Transactions
            .Where(t => t.AccountId == id)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        bool isLiability = account.AccountType.Name == "Liability";

        // System categories (e.g. Opening Balance) are a neutral starting point — always add.
        // For regular transactions:
        //   Assets:      income adds, expense subtracts.
        //   Liabilities: expense adds (increases what you owe), income subtracts (e.g. refund).
        decimal balance = transactions.Sum(t =>
        {
            if (t.Category.IsSystem) return t.Amount;
            bool isIncome = t.Category.CategoryType.Name == "Income";
            bool addsToBalance = isLiability ? !isIncome : isIncome;
            return addsToBalance ? t.Amount : -t.Amount;
        });

        // Liability payments reduce the balance on both sides:
        //   Asset account:     money leaves  → subtract the payment amount
        //   Liability account: debt reduces  → subtract the payment amount
        var paymentsOut = await db.LiabilityPayments
            .Where(p => p.AssetAccountId == id)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        var paymentsIn = await db.LiabilityPayments
            .Where(p => p.LiabilityAccountId == id)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        balance -= paymentsOut;
        balance -= paymentsIn;

        return balance;
    }
}
