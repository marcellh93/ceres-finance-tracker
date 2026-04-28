using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class ReportService(AppDbContext db) : IReportService
{
    public async Task<IReadOnlyList<NetWorthEntry>> GetNetWorthAsync()
    {
        // Load all active accounts with their currency and transactions (including category type).
        var accounts = await db.Accounts
            .Where(a => a.IsActive)
            .Include(a => a.Currency)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId) || accountIds.Contains(p.LiabilityAccountId))
            .ToListAsync();

        var paymentsByAsset = liabilityPayments
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = liabilityPayments
            .GroupBy(p => p.LiabilityAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var grouped = accounts
            .GroupBy(a => new { a.Currency.Code, a.Currency.Symbol, a.CurrencyId })
            .Select(g =>
            {
                decimal assets = 0;
                decimal liabilities = 0;

                foreach (var account in g)
                {
                    bool isLiability = account.AccountType.Name == "Liability";

                    var balance = account.Transactions.Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                    balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                    balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                    if (!isLiability)
                        assets += balance;
                    else
                        liabilities += balance;
                }

                return new NetWorthEntry(g.Key.Code, g.Key.Symbol, assets, liabilities, assets - liabilities);
            })
            .ToList();

        return grouped;
    }

    public async Task<IncomeExpenseSummary> GetIncomeExpenseSummaryAsync(int currencyId, DateOnly from, DateOnly to)
    {
        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        var transactions = await db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var income = transactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var expenses = transactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);
        var savings = income > 0 ? (income - expenses) / income : 0;

        return new IncomeExpenseSummary(currency.Code, currency.Symbol, income, expenses, savings);
    }

    public async Task<ExpenseBreakdown> GetExpenseBreakdownAsync(int currencyId, DateOnly from, DateOnly to)
    {
        var currency = await db.Currencies.FindAsync(currencyId)
            ?? throw new InvalidOperationException($"Currency {currencyId} not found.");

        var transactions = await db.Transactions
            .Where(t =>
                t.Date >= from &&
                t.Date <= to &&
                t.Account.CurrencyId == currencyId &&
                t.Category.CategoryType.Name == "Expense")
            .Include(t => t.Category)
            .ToListAsync();

        var categories = transactions
            .GroupBy(t => new { t.Category.Name, t.Category.LifestyleTag })
            .Select(g => new CategoryExpense(g.Key.Name, g.Key.LifestyleTag, g.Sum(t => t.Amount)))
            .OrderByDescending(c => c.Total)
            .ToList();

        return new ExpenseBreakdown(currency.Code, currency.Symbol, categories);
    }

    public async Task<IReadOnlyList<Transaction>> GetTransactionHistoryAsync(
        int currencyId,
        DateOnly from,
        DateOnly to,
        Guid? accountId = null,
        Guid? categoryId = null,
        int limit = 50,
        int offset = 0)
    {
        var query = db.Transactions
            .Where(t => t.Date >= from && t.Date <= to && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Account)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .AsQueryable();

        if (accountId.HasValue)
            query = query.Where(t => t.AccountId == accountId.Value);
        if (categoryId.HasValue)
            query = query.Where(t => t.CategoryId == categoryId.Value);

        return await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
