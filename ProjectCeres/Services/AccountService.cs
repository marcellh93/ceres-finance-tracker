using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class AccountService(AppDbContext db, ICurrentUserAccessor user) : IAccountService
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
        var accountType = await db.AccountTypes.FindAsync(vm.AccountTypeId!.Value);
        if (accountType?.Name == "Liability")
            ValidateLiabilityRepaymentFields(vm.LiabilityRepaymentType, vm.InterestRate);

        var account = new Account
        {
            Id                     = Guid.NewGuid(),
            Name                   = vm.Name,
            AccountTypeId          = vm.AccountTypeId!.Value,
            CurrencyId             = vm.CurrencyId!.Value,
            Description            = vm.Description,
            LiabilityRepaymentType = vm.LiabilityRepaymentType,
            InterestRate           = vm.InterestRate,
            IsActive               = true,
            ExcludeFromSpendable   = accountType?.Name == "Liability" ? false : vm.ExcludeFromSpendable,
            UserId                 = user.UserId,
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
                CreatedAt   = DateTime.UtcNow,
                UserId      = user.UserId,
            });
        }

        await db.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAsync(AccountEditViewModel vm)
    {
        var account = await db.Accounts
            .Include(a => a.AccountType)
            .FirstOrDefaultAsync(a => a.Id == vm.Id)
            ?? throw new InvalidOperationException($"Account {vm.Id} not found.");

        if (account.AccountType.Name == "Liability")
            ValidateLiabilityRepaymentFields(vm.LiabilityRepaymentType, vm.InterestRate);

        account.Name                   = vm.Name;
        account.Description            = vm.Description;
        account.LiabilityRepaymentType = vm.LiabilityRepaymentType;
        account.InterestRate           = vm.InterestRate;
        account.ExcludeFromSpendable   = account.AccountType.Name == "Liability" ? false : vm.ExcludeFromSpendable;

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
                UserId      = user.UserId,
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

        var transfersOut = await db.Transfers
            .Where(t => t.SourceAccountId == id)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var transfersIn = await db.Transfers
            .Where(t => t.DestAccountId == id)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        balance += transfersIn;
        balance -= transfersOut;

        return balance;
    }

    private static void ValidateLiabilityRepaymentFields(string? repaymentType, decimal? interestRate)
    {
        if (repaymentType == "Amortising" && interestRate is null)
            throw new InvalidOperationException("An Amortising liability must have an interest rate.");

        if (repaymentType == "FullMonthly" && interestRate is not null)
            throw new InvalidOperationException("A FullMonthly liability must not have an interest rate.");
    }

    public async Task<AccountLedgerViewModel?> GetLedgerAsync(Guid id)
    {
        var account = await db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (account is null) return null;

        bool isLiability = account.AccountType.Name == "Liability";

        // --- Transactions (including Opening Balance) ---
        var transactions = await db.Transactions
            .Where(t => t.AccountId == id)
            .Include(t => t.Category).ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var txEntries = transactions.Select(t =>
        {
            decimal signed;
            string entryType;

            if (t.Category.IsSystem)
            {
                signed    = t.Amount;
                entryType = "Opening Balance";
            }
            else
            {
                bool isIncome      = t.Category.CategoryType.Name == "Income";
                bool addsToBalance = isLiability ? !isIncome : isIncome;
                signed    = addsToBalance ? t.Amount : -t.Amount;
                entryType = "Transaction";
            }

            return new LedgerEntryViewModel
            {
                Date         = t.Date,
                CreatedAt    = t.CreatedAt,
                Description  = t.Description ?? t.Category.Name,
                EntryType    = entryType,
                CategoryName = t.Category.IsSystem ? null : t.Category.Name,
                SignedAmount = signed
            };
        });

        // --- Transfers ---
        var transfersOut = await db.Transfers
            .Where(t => t.SourceAccountId == id)
            .Include(t => t.DestAccount)
            .ToListAsync();

        var transfersIn = await db.Transfers
            .Where(t => t.DestAccountId == id)
            .Include(t => t.SourceAccount)
            .ToListAsync();

        var transferEntries = transfersOut.Select(t => new LedgerEntryViewModel
        {
            Date         = t.Date,
            CreatedAt    = t.CreatedAt,
            Description  = t.Description ?? $"Transfer to {t.DestAccount.Name}",
            EntryType    = "Transfer",
            SignedAmount = -t.Amount
        }).Concat(transfersIn.Select(t => new LedgerEntryViewModel
        {
            Date         = t.Date,
            CreatedAt    = t.CreatedAt,
            Description  = t.Description ?? $"Transfer from {t.SourceAccount.Name}",
            EntryType    = "Transfer",
            SignedAmount = t.Amount
        }));

        // --- Liability payments ---
        var paymentsOut = await db.LiabilityPayments
            .Where(p => p.AssetAccountId == id)
            .Include(p => p.LiabilityAccount)
            .ToListAsync();

        var paymentsIn = await db.LiabilityPayments
            .Where(p => p.LiabilityAccountId == id)
            .Include(p => p.AssetAccount)
            .ToListAsync();

        var paymentEntries = paymentsOut.Select(p => new LedgerEntryViewModel
        {
            Date         = p.Date,
            CreatedAt    = p.CreatedAt,
            Description  = p.Description ?? $"Payment to {p.LiabilityAccount.Name}",
            EntryType    = "Liability Payment",
            SignedAmount = -p.Amount
        }).Concat(paymentsIn.Select(p => new LedgerEntryViewModel
        {
            Date         = p.Date,
            CreatedAt    = p.CreatedAt,
            Description  = p.Description ?? $"Payment from {p.AssetAccount.Name}",
            EntryType    = "Liability Payment",
            SignedAmount = -p.Amount
        }));

        // --- Merge, sort, compute running balance ---
        var allEntries = txEntries
            .Concat(transferEntries)
            .Concat(paymentEntries)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAt)
            .ToList();

        decimal running = 0;
        foreach (var entry in allEntries)
        {
            running             += entry.SignedAmount;
            entry.RunningBalance = running;
        }

        return new AccountLedgerViewModel
        {
            AccountId      = account.Id,
            AccountName    = account.Name,
            CurrencySymbol = account.Currency.Symbol,
            Entries        = allEntries
        };
    }

    // -------------------------------------------------------------------------
    // API surface (Result-returning).
    // -------------------------------------------------------------------------

    public async Task<Result<Account>> TryCreateAsync(CreateAccountRequest request)
    {
        var accountType = await db.AccountTypes.FindAsync(request.AccountTypeId!.Value);
        if (accountType is null)
            return Result<Account>.Fail(AccountPolicies.InvalidAccountTypeCode, "The selected account type does not exist.");

        var currencyExists = await db.Currencies.AnyAsync(c => c.Id == request.CurrencyId!.Value);
        if (!currencyExists)
            return Result<Account>.Fail(AccountPolicies.InvalidCurrencyCode, "The selected currency does not exist.");

        if (accountType.Name == "Liability")
        {
            var policy = AccountPolicies.ValidateLiabilityRepayment(request.LiabilityRepaymentType, request.InterestRate);
            if (!policy.IsSuccess)
                return Result<Account>.Fail(policy.Error!.Value.Code, policy.Error!.Value.Message);
        }

        var account = new Account
        {
            Id                     = Guid.NewGuid(),
            Name                   = request.Name.Trim(),
            AccountTypeId          = request.AccountTypeId!.Value,
            CurrencyId             = request.CurrencyId!.Value,
            Description            = request.Description,
            LiabilityRepaymentType = request.LiabilityRepaymentType,
            InterestRate           = request.InterestRate,
            IsActive               = true,
            ExcludeFromSpendable   = accountType.Name == "Liability" ? false : request.ExcludeFromSpendable,
            UserId                 = user.UserId,
        };
        db.Accounts.Add(account);

        if (request.OpeningBalance != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                Date        = request.OpeningBalanceDate,
                Amount      = Math.Abs(request.OpeningBalance),
                Description = "Opening Balance",
                AccountId   = account.Id,
                CategoryId  = OpeningBalanceCategoryId,
                CreatedAt   = DateTime.UtcNow,
                UserId      = user.UserId,
            });
        }

        await db.SaveChangesAsync();

        var fresh = await db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .FirstAsync(a => a.Id == account.Id);
        return Result<Account>.Ok(fresh);
    }

    public async Task<Result<Account>> TryUpdateAsync(Guid id, UpdateAccountRequest request)
    {
        var account = await db.Accounts
            .Include(a => a.AccountType)
            .Include(a => a.Currency)
            .Owned(user)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return Result<Account>.Fail("NOT_FOUND", "Account not found.");

        if (account.AccountType.Name == "Liability")
        {
            var policy = AccountPolicies.ValidateLiabilityRepayment(request.LiabilityRepaymentType, request.InterestRate);
            if (!policy.IsSuccess)
                return Result<Account>.Fail(policy.Error!.Value.Code, policy.Error!.Value.Message);
        }

        account.Name                   = request.Name.Trim();
        account.Description            = request.Description;
        account.LiabilityRepaymentType = request.LiabilityRepaymentType;
        account.InterestRate           = request.InterestRate;
        account.ExcludeFromSpendable   = account.AccountType.Name == "Liability" ? false : request.ExcludeFromSpendable;

        // Manage the opening balance transaction (system-managed).
        var existingOb = await db.Transactions
            .Owned(user)
            .FirstOrDefaultAsync(t => t.AccountId == id && t.CategoryId == OpeningBalanceCategoryId);

        if (existingOb is not null)
        {
            if (request.OpeningBalance == 0) db.Transactions.Remove(existingOb);
            else
            {
                existingOb.Amount = Math.Abs(request.OpeningBalance);
                existingOb.Date   = request.OpeningBalanceDate;
            }
        }
        else if (request.OpeningBalance != 0)
        {
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                Date        = request.OpeningBalanceDate,
                Amount      = Math.Abs(request.OpeningBalance),
                Description = "Opening Balance",
                AccountId   = id,
                CategoryId  = OpeningBalanceCategoryId,
                CreatedAt   = DateTime.UtcNow,
                UserId      = user.UserId,
            });
        }

        await db.SaveChangesAsync();
        return Result<Account>.Ok(account);
    }

    public async Task<Result> TryDeactivateAsync(Guid id)
    {
        var account = await db.Accounts.Owned(user).FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return Result.Fail("NOT_FOUND", "Account not found.");
        account.IsActive = false;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
