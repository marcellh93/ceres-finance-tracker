using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Services;

public class DashboardService(AppDbContext db, ISettingsService settingsService) : IDashboardService
{
    public async Task<DashboardData> GetDashboardDataAsync()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;
        var currency   = settings.DefaultCurrency;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var mtdFrom = new DateOnly(today.Year, today.Month, 1);

        // MTD transactions for the default currency.
        var mtdTransactions = await db.Transactions
            .Where(t => t.Date >= mtdFrom && t.Date <= today && t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var mtdIncome   = mtdTransactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var mtdExpenses = mtdTransactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);
        var savingsRate = mtdIncome > 0 ? (mtdIncome - mtdExpenses) / mtdIncome : 0;

        // Net worth across all currencies.
        var reportService = new ReportService(db);
        var netWorth = await reportService.GetNetWorthAsync();

        // Pending reminders: active recurring transactions whose NextDueDate <= today.
        var pendingCount = await db.RecurringTransactions
            .CountAsync(r => r.IsActive && r.NextDueDate <= today);

        return new DashboardData(
            NetWorth:              netWorth,
            MtdIncome:             mtdIncome,
            MtdExpenses:           mtdExpenses,
            SavingsRate:           savingsRate,
            PendingRemindersCount: pendingCount,
            CurrencyCode:          currency.Code,
            CurrencySymbol:        currency.Symbol);
    }

    public async Task<HealthSnapshotData> GetHealthSnapshotAsync()
    {
        var settings   = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;
        var currency   = settings.DefaultCurrency;

        var spendable    = await GetSpendableBalanceAsync(currencyId);
        var runway       = await GetRunwayAsync(currencyId);
        var incomeMetrics = await GetIncomeMetricsAsync(currencyId);
        var burnRate     = await GetBudgetBurnRateAsync(currencyId);

        return new HealthSnapshotData(
            SpendableBalance:    spendable,
            RunwayMonths:        runway,
            CurrentMonthIncome:  incomeMetrics.currentMonth,
            RollingAverageIncome: incomeMetrics.rollingAverage,
            IncomeDeltaPercent:  incomeMetrics.deltaPercent,
            BudgetBurnRate:      burnRate,
            CurrencySymbol:      currency.Symbol,
            CurrencyCode:        currency.Code);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spendable balance = SUM of non-excluded asset account balances − SUM of
    /// EstimatedAmount for qualifying recurring transactions (due this month).
    /// Liability accounts are never included regardless of ExcludeFromSpendable.
    /// Returns null if there are no qualifying accounts.
    /// </summary>
    private async Task<decimal?> GetSpendableBalanceAsync(int currencyId)
    {
        var today      = DateOnly.FromDateTime(DateTime.Today);
        var firstDay   = new DateOnly(today.Year, today.Month, 1);
        var lastDay    = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Load active, non-excluded asset accounts with their transactions and category types.
        var accounts = await db.Accounts
            .Where(a => a.IsActive
                     && a.CurrencyId == currencyId
                     && a.AccountType.Name == "Asset"
                     && !a.ExcludeFromSpendable)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        if (accounts.Count == 0)
            return null;

        // Derive balance per account (same sign logic as ReportService).
        decimal totalBalance = 0m;
        foreach (var account in accounts)
        {
            totalBalance += account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome = t.Category.CategoryType.Name == "Income";
                return isIncome ? t.Amount : -t.Amount;
            });
        }

        // Load liability payments that touch these asset accounts.
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId))
            .ToListAsync();
        totalBalance -= liabilityPayments.Sum(p => p.Amount);

        // Subtract recurring transactions due this calendar month.
        var dueRecurring = await db.RecurringTransactions
            .Where(r => r.IsActive
                     && r.EstimatedAmount != null
                     && r.NextDueDate >= firstDay
                     && r.NextDueDate <= lastDay
                     && r.Account.CurrencyId == currencyId)
            .ToListAsync();

        var dueTotal = dueRecurring.Sum(r => r.EstimatedAmount ?? 0m);
        return totalBalance - dueTotal;
    }

    /// <summary>
    /// Runway = (total assets − total liabilities) ÷ avg monthly expenses over last 6 full months.
    /// Returns null if avg monthly expenses = 0 or no expense transactions exist in that window.
    /// </summary>
    private async Task<decimal?> GetRunwayAsync(int currencyId)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var sixStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-6);
        var sixEnd   = new DateOnly(today.Year, today.Month, 1).AddDays(-1); // last day of month -1

        // Load all active accounts for this currency with transactions and category types.
        var accounts = await db.Accounts
            .Where(a => a.IsActive && a.CurrencyId == currencyId)
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

        var paymentsByAsset     = liabilityPayments.GroupBy(p => p.AssetAccountId)
                                                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = liabilityPayments.GroupBy(p => p.LiabilityAccountId)
                                                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        decimal totalAssets      = 0m;
        decimal totalLiabilities = 0m;

        foreach (var account in accounts)
        {
            bool isLiability = account.AccountType.Name == "Liability";

            var balance = account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome    = t.Category.CategoryType.Name == "Income";
                bool addsBalance = isLiability ? !isIncome : isIncome;
                return addsBalance ? t.Amount : -t.Amount;
            });

            balance -= paymentsByAsset.GetValueOrDefault(account.Id);
            balance -= paymentsByLiability.GetValueOrDefault(account.Id);

            if (!isLiability)
                totalAssets += balance;
            else
                totalLiabilities += balance;
        }

        var netWorth = totalAssets - totalLiabilities;

        // Average monthly expenses over last 6 full calendar months.
        var expenseTransactions = await db.Transactions
            .Where(t => t.Date >= sixStart
                     && t.Date <= sixEnd
                     && t.Account.CurrencyId == currencyId
                     && t.Category.CategoryType.Name == "Expense"
                     && !t.Category.IsSystem)
            .ToListAsync();

        if (expenseTransactions.Count == 0)
            return null;

        var totalExpenses = expenseTransactions.Sum(t => t.Amount);
        var avgMonthlyExpenses = totalExpenses / 6m;

        if (avgMonthlyExpenses == 0m)
            return null;

        return netWorth / avgMonthlyExpenses;
    }

    /// <summary>
    /// Income metrics:
    ///   rollingAverage  = avg monthly income over last 6 full months
    ///   currentMonth    = income so far this calendar month
    ///   deltaPercent    = (currentMonth − rollingAverage) / rollingAverage
    /// All values are null if no income exists in the prior 6 months (rollingAverage = 0).
    /// </summary>
    private async Task<(decimal? currentMonth, decimal? rollingAverage, decimal? deltaPercent)> GetIncomeMetricsAsync(int currencyId)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var mtdFrom  = new DateOnly(today.Year, today.Month, 1);
        var sixStart = mtdFrom.AddMonths(-6);
        var sixEnd   = mtdFrom.AddDays(-1);

        // Current month income.
        var currentMonthIncome = await db.Transactions
            .Where(t => t.Date >= mtdFrom
                     && t.Date <= today
                     && t.Account.CurrencyId == currencyId
                     && t.Category.CategoryType.Name == "Income"
                     && !t.Category.IsSystem)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        // Prior 6 full months income.
        var priorIncomeTransactions = await db.Transactions
            .Where(t => t.Date >= sixStart
                     && t.Date <= sixEnd
                     && t.Account.CurrencyId == currencyId
                     && t.Category.CategoryType.Name == "Income"
                     && !t.Category.IsSystem)
            .ToListAsync();

        if (priorIncomeTransactions.Count == 0)
            return (currentMonthIncome, null, null);

        var rollingAverage = priorIncomeTransactions.Sum(t => t.Amount) / 6m;

        if (rollingAverage == 0m)
            return (currentMonthIncome, 0m, null);

        var deltaPercent = (currentMonthIncome - rollingAverage) / rollingAverage;

        return (currentMonthIncome, rollingAverage, deltaPercent);
    }

    /// <summary>
    /// Budget burn rate = SUM(actual spend this month for active CategoryBudgets) ÷
    ///                    SUM(limit across those same budgets).
    /// Returns null if no active CategoryBudgets exist for the default currency.
    /// </summary>
    private async Task<decimal?> GetBudgetBurnRateAsync(int currencyId)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var mtdFrom = new DateOnly(today.Year, today.Month, 1);

        var activeBudgets = await db.CategoryBudgets
            .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
            .ToListAsync();

        if (activeBudgets.Count == 0)
            return null;

        var totalLimit = activeBudgets.Sum(cb => cb.LimitAmount);
        if (totalLimit == 0m)
            return null;

        // Actual spend this month across all active budget categories.
        var budgetCategoryIds = activeBudgets.Select(cb => cb.CategoryId).ToList();

        var actualSpend = await db.Transactions
            .Where(t => t.Date >= mtdFrom
                     && t.Date <= today
                     && budgetCategoryIds.Contains(t.CategoryId)
                     && t.Account.CurrencyId == currencyId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return actualSpend / totalLimit;
    }
}
