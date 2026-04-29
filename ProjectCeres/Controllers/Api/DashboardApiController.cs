using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
public class DashboardApiController(
    IDashboardService dashboardService,
    ICategoryBudgetService categoryBudgetService,
    IBudgetService budgetService,
    ISettingsService settingsService,
    IAccountService accountService,
    AppDbContext db) : ControllerBase
{
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var snapshot = await dashboardService.GetHealthSnapshotAsync();
        return Ok(snapshot);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var data = await dashboardService.GetDashboardDataAsync();

        var summary = new DashboardSummaryDto(
            NetWorth: data.NetWorth,
            Mtd: new MtdSummary(
                CurrencyCode:   data.CurrencyCode,
                CurrencySymbol: data.CurrencySymbol,
                Income:         data.MtdIncome,
                Expenses:       data.MtdExpenses,
                SavingsRate:    data.SavingsRate),
            RemindersDueCount: data.PendingRemindersCount);

        return Ok(summary);
    }

    [HttpGet("category-budgets")]
    public async Task<IActionResult> GetCategoryBudgets()
    {
        var budgets = await categoryBudgetService.GetAllAsync(includeInactive: false);
        var now = DateTime.Today;

        var result = new List<object>();
        foreach (var budget in budgets)
        {
            var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, now.Year, now.Month);
            var percentUsed = budget.LimitAmount == 0m
                ? 0m
                : Math.Round(spent / budget.LimitAmount * 100m, 2);

            result.Add(new
            {
                id             = budget.Id,
                categoryName   = budget.Category.Name,
                currencyCode   = budget.Currency.Code,
                currencySymbol = budget.Currency.Symbol,
                spent          = spent,
                limit          = budget.LimitAmount,
                percentUsed    = percentUsed
            });
        }

        return Ok(result);
    }

    [HttpGet("goal-budgets")]
    public async Task<IActionResult> GetGoalBudgets()
    {
        var goals = await budgetService.GetAllAsync(includeInactive: false);

        var result = new List<object>();
        foreach (var goal in goals)
        {
            var progress = await budgetService.GetProgressAsync(goal.Id);

            result.Add(new
            {
                id             = goal.Id,
                name           = goal.Name,
                goalType       = goal.GoalType,
                amountProgress = progress.AmountProgress,
                targetAmount   = progress.TargetAmount,
                remaining      = progress.Remaining,
                percentUsed    = progress.PercentUsed,
                currencyCode   = goal.Currency.Code,
                currencySymbol = goal.Currency.Symbol
            });
        }

        return Ok(result);
    }

    [HttpGet("net-worth-trend")]
    public async Task<IActionResult> GetNetWorthTrend()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

        var today = DateOnly.FromDateTime(DateTime.Today);

        var accounts = await db.Accounts
            .Where(a => a.IsActive && a.CurrencyId == currencyId)
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .AsNoTracking()
            .ToListAsync();

        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var allLiabilityPayments = await db.LiabilityPayments
            .Where(p => accountIds.Contains(p.AssetAccountId) || accountIds.Contains(p.LiabilityAccountId))
            .AsNoTracking()
            .ToListAsync();

        var points = new List<NetWorthTrendPoint>();

        for (int i = 11; i >= 0; i--)
        {
            var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(-i + 1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var monthLabel = new DateOnly(monthEnd.Year, monthEnd.Month, 1);

            var paymentsUpToMonth = allLiabilityPayments.Where(p => p.Date <= monthEnd).ToList();
            var paymentsByAsset = paymentsUpToMonth
                .GroupBy(p => p.AssetAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var paymentsByLiability = paymentsUpToMonth
                .GroupBy(p => p.LiabilityAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            decimal assets = 0;
            decimal liabilities = 0;

            foreach (var account in accounts)
            {
                bool isLiability = account.AccountType.Name == "Liability";

                var balance = account.Transactions
                    .Where(t => t.Date <= monthEnd)
                    .Sum(t =>
                    {
                        if (t.Category.IsSystem) return t.Amount;
                        bool isIncome = t.Category.CategoryType.Name == "Income";
                        bool addsToBalance = isLiability ? !isIncome : isIncome;
                        return addsToBalance ? t.Amount : -t.Amount;
                    });

                balance -= paymentsByAsset.GetValueOrDefault(account.Id);
                balance -= paymentsByLiability.GetValueOrDefault(account.Id);

                if (!isLiability) assets += balance;
                else liabilities += balance;
            }

            points.Add(new NetWorthTrendPoint(
                Month: monthLabel.ToString("yyyy-MM"),
                Assets: Math.Round(assets, 2),
                Liabilities: Math.Round(liabilities, 2),
                NetWorth: Math.Round(assets - liabilities, 2)));
        }

        return Ok(new NetWorthTrendDto(currency.Code, currency.Symbol, points));
    }

    [HttpGet("income-expense")]
    public async Task<IActionResult> GetIncomeExpense()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;
        var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);

        var allTransactions = await db.Transactions
            .Where(t => t.Date >= windowStart && t.Date <= today &&
                        t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .AsNoTracking()
            .ToListAsync();

        var points = new List<IncomeExpensePoint>();

        for (int i = 11; i >= 0; i--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var monthTx = allTransactions.Where(t => t.Date >= monthStart && t.Date <= monthEnd).ToList();
            var income   = monthTx.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
            var expenses = monthTx.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

            points.Add(new IncomeExpensePoint(
                Month: monthStart.ToString("yyyy-MM"),
                Income: Math.Round(income, 2),
                Expenses: Math.Round(expenses, 2)));
        }

        return Ok(new IncomeExpenseDto(currency.Code, currency.Symbol, points));
    }

    [HttpGet("spending-by-category")]
    public async Task<IActionResult> GetSpendingByCategory()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var transactions = await db.Transactions
            .Where(t => t.Date >= monthStart && t.Date <= today &&
                        t.Account.CurrencyId == currencyId &&
                        t.Category.CategoryType.Name == "Expense" &&
                        !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .AsNoTracking()
            .ToListAsync();

        var result = transactions
            .GroupBy(t => t.Category.Name)
            .Select(g => new
            {
                categoryName = g.Key,
                amount       = Math.Round(g.Sum(t => t.Amount), 2)
            })
            .OrderByDescending(x => x.amount)
            .ToList<object>();

        return Ok(result);
    }

    [HttpGet("account-balances")]
    public async Task<IActionResult> GetAccountBalances()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var accounts = await db.Accounts
            .Where(a => a.IsActive && a.CurrencyId == currencyId && a.AccountType.Name != "Liability")
            .AsNoTracking()
            .ToListAsync();

        var rows = new List<(string Name, decimal Balance)>();
        foreach (var account in accounts)
        {
            var balance = await accountService.GetBalanceAsync(account.Id);
            rows.Add((account.Name, Math.Round(balance, 2)));
        }

        var result = rows
            .OrderByDescending(r => r.Balance)
            .Select(r => new { accountName = r.Name, balance = r.Balance })
            .ToList<object>();

        return Ok(result);
    }

    [HttpGet("cash-flow")]
    public async Task<IActionResult> GetCashFlow()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-5);

        // Load all transactions for the 6-month window once — avoids N+1
        var allTransactions = await db.Transactions
            .Where(t => t.Date >= windowStart && t.Date <= today &&
                        t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .AsNoTracking()
            .ToListAsync();

        var result = new List<object>();

        for (int i = 5; i >= 0; i--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var monthTx  = allTransactions.Where(t => t.Date >= monthStart && t.Date <= monthEnd).ToList();
            var income   = monthTx.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
            var expenses = monthTx.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

            result.Add(new
            {
                month   = monthStart.ToString("yyyy-MM"),
                netFlow = Math.Round(income - expenses, 2)
            });
        }

        return Ok(result);
    }
}
