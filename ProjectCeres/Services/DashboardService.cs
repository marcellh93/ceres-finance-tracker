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

        var (availableToday, safeToSpend, imminentBills, laterBills, budgetReserve) = await GetSpendableBalanceAsync(currencyId);
        var (runway, avgMonthlyExpense) = await GetRunwayAsync(currencyId);
        var incomeMetrics = await GetIncomeMetricsAsync(currencyId);
        var (burnRate, budgetSpent, budgetTotal) = await GetBudgetBurnRateAsync(currencyId);

        return new HealthSnapshotData(
            AvailableToday:       availableToday,
            SafeToSpend:          safeToSpend,
            ImminentBills:        imminentBills,
            LaterBills:           laterBills,
            BudgetReserve:        budgetReserve,
            RunwayMonths:         runway,
            AvgMonthlyExpense:    avgMonthlyExpense,
            CurrentMonthIncome:   incomeMetrics.currentMonth,
            RollingAverageIncome: incomeMetrics.rollingAverage,
            IncomeDeltaPercent:   incomeMetrics.deltaPercent,
            BudgetBurnRate:       burnRate,
            BudgetSpentMtd:       budgetSpent,
            BudgetTotalLimit:     budgetTotal,
            CurrencySymbol:       currency.Symbol,
            CurrencyCode:         currency.Code);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Layered spendable balance calculation.
    ///
    /// AvailableToday = liquid balance of non-excluded asset accounts
    ///                  − imminent bills (due within 7 days or overdue, this calendar month)
    ///
    /// LaterBills     = recurring bills due this calendar month but outside the 7-day window
    ///
    /// BudgetReserve  = SUM(MAX(0, limit − actual spend this month)) per active CategoryBudget
    ///
    /// SafeToSpend    = AvailableToday − LaterBills − BudgetReserve
    ///
    /// Returns all nulls if there are no qualifying accounts.
    /// </summary>
    private async Task<(decimal? availableToday, decimal? safeToSpend, decimal? imminentBills, decimal? laterBills, decimal? budgetReserve)> GetSpendableBalanceAsync(int currencyId)
    {
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var firstDay = new DateOnly(today.Year, today.Month, 1);
        var lastDay  = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        const int imminentWindowDays = 7;

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
            return (null, null, null, null, null);

        // Derive liquid balance per account (same sign logic as AccountService).
        decimal liquid = 0m;
        foreach (var account in accounts)
        {
            liquid += account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome = t.Category.CategoryType.Name == "Income";
                return isIncome ? t.Amount : -t.Amount;
            });
        }

        // Subtract liability payments that source from these asset accounts.
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AssetAccountId))
            .ToListAsync();
        liquid -= liabilityPayments.Sum(p => p.Amount);

        // Include transfers (cross-boundary transfers must be counted to match displayed balances).
        var transfersIn = await db.Transfers
            .Where(t => accountIds.Contains(t.DestAccountId))
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        var transfersOut = await db.Transfers
            .Where(t => accountIds.Contains(t.SourceAccountId))
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;
        liquid += transfersIn;
        liquid -= transfersOut;

        // Imminent: due between start of this month (catches overdue) and today+7, inclusive.
        // The +7 window intentionally crosses calendar month boundaries so a bill due May 1
        // when today is April 28 is correctly treated as imminent.
        // Later: due after today+7 through end of this calendar month only.
        var imminentCutoff = today.AddDays(imminentWindowDays);

        var imminentRecurring = await db.RecurringTransactions
            .Where(r => r.IsActive
                     && r.EstimatedAmount != null
                     && r.NextDueDate >= firstDay
                     && r.NextDueDate <= imminentCutoff
                     && accountIds.Contains(r.AccountId))
            .ToListAsync();
        decimal imminentBills = imminentRecurring.Sum(r => r.EstimatedAmount ?? 0m);

        var laterRecurring = await db.RecurringTransactions
            .Where(r => r.IsActive
                     && r.EstimatedAmount != null
                     && r.NextDueDate > imminentCutoff
                     && r.NextDueDate <= lastDay
                     && accountIds.Contains(r.AccountId))
            .ToListAsync();
        decimal laterBills = laterRecurring.Sum(r => r.EstimatedAmount ?? 0m);

        // Budget reserve = SUM(MAX(0, limit − actual spend this PERIOD)) per active CategoryBudget.
        // Uses BudgetPeriod helper so the cycle respects Settings.BudgetPeriodStartDay,
        // not the calendar month.
        var activeBudgets = await db.CategoryBudgets
            .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
            .ToListAsync();

        decimal budgetReserve = 0m;
        if (activeBudgets.Count > 0)
        {
            var settings = await db.Settings.FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Settings row missing.");
            var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(today, settings.BudgetPeriodStartDay);
            var (periodStart, periodEnd) = BudgetPeriod.GetBoundsForMonth(year, month, settings.BudgetPeriodStartDay);

            var budgetCategoryIds = activeBudgets.Select(cb => cb.CategoryId).ToList();
            var actualSpendByCategory = await db.Transactions
                .Where(t => t.Date >= periodStart
                         && t.Date <= periodEnd
                         && budgetCategoryIds.Contains(t.CategoryId)
                         && t.Account.CurrencyId == currencyId)
                .GroupBy(t => t.CategoryId)
                .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
                .ToListAsync();

            var spendMap = actualSpendByCategory.ToDictionary(x => x.CategoryId, x => x.Total);
            foreach (var budget in activeBudgets)
            {
                var actual  = spendMap.GetValueOrDefault(budget.CategoryId);
                var reserve = budget.LimitAmount - actual;
                if (reserve > 0)
                    budgetReserve += reserve;
            }
        }

        var availableToday = liquid - imminentBills;
        var safeToSpend    = availableToday - laterBills - budgetReserve;

        return (availableToday, safeToSpend, imminentBills, laterBills, budgetReserve);
    }

    /// <summary>
    /// Runway = (total assets − total liabilities) ÷ avg monthly expenses over last 6 full months.
    /// Returns null if avg monthly expenses = 0 or no expense transactions exist in that window.
    /// </summary>
    private async Task<(decimal? months, decimal? avgMonthlyExpense)> GetRunwayAsync(int currencyId)
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
            return (null, null);

        var totalExpenses = expenseTransactions.Sum(t => t.Amount);
        var avgMonthlyExpenses = totalExpenses / 6m;

        if (avgMonthlyExpenses == 0m)
            return (null, null);

        return (netWorth / avgMonthlyExpenses, avgMonthlyExpenses);
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
    private async Task<(decimal? burnRate, decimal? spent, decimal? totalLimit)> GetBudgetBurnRateAsync(int currencyId)
    {
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var mtdFrom = new DateOnly(today.Year, today.Month, 1);

        var activeBudgets = await db.CategoryBudgets
            .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
            .ToListAsync();

        if (activeBudgets.Count == 0)
            return (null, null, null);

        var totalLimit = activeBudgets.Sum(cb => cb.LimitAmount);
        if (totalLimit == 0m)
            return (null, null, null);

        // Actual spend this month across all active budget categories.
        var budgetCategoryIds = activeBudgets.Select(cb => cb.CategoryId).ToList();

        var actualSpend = await db.Transactions
            .Where(t => t.Date >= mtdFrom
                     && t.Date <= today
                     && budgetCategoryIds.Contains(t.CategoryId)
                     && t.Account.CurrencyId == currencyId)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return (actualSpend / totalLimit, actualSpend, totalLimit);
    }
}
