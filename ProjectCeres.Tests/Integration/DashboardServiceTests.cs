using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for DashboardService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// DashboardService is MTD-scoped (month-to-date from 1st of the current month through today)
/// and uses the default currency from Settings (seeded as EUR, CurrencyId = 1).
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary  (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class DashboardServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private DashboardService _service = null!;
    private AccountService _accountService = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db);
        var settingsService = new SettingsService(_fixture.Db);
        _service = new DashboardService(_fixture.Db, settingsService);

        // Ensure settings row exists (required by DashboardService).
        await settingsService.EnsureExistsAsync();

        // Create a reusable EUR asset account with no opening balance.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Dashboard Test Account {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        _accountId = account.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void AddMtdTransaction(Guid categoryId, decimal amount)
    {
        // Place the transaction on the 1st of the current month so it is always MTD.
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var mtdDate = new DateOnly(today.Year, today.Month, 1);

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = mtdDate,
            Amount     = amount,
            AccountId  = _accountId,
            CategoryId = categoryId,
            CreatedAt  = DateTime.UtcNow
        });
    }

    // -------------------------------------------------------------------------
    // GetDashboardDataAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_ReturnsCurrencyFromSettings()
    {
        var data = await _service.GetDashboardDataAsync();

        // Default currency is EUR (CurrencyId = 1, Code = "EUR").
        data.CurrencyCode.Should().Be("EUR");
        data.CurrencySymbol.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDashboardDataAsync_SumsMtdIncomeAndExpenses()
    {
        AddMtdTransaction(SalaryCategoryId,  2000m);
        AddMtdTransaction(HousingCategoryId,  600m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.MtdIncome.Should().BeGreaterThanOrEqualTo(2000m);
        data.MtdExpenses.Should().BeGreaterThanOrEqualTo(600m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_SavingsRateIsZero_WhenNoMtdIncome()
    {
        AddMtdTransaction(HousingCategoryId, 300m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.SavingsRate.Should().Be(0m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_SavingsRateReflectsIncomeMinusExpenses()
    {
        AddMtdTransaction(SalaryCategoryId,  1000m);
        AddMtdTransaction(HousingCategoryId,  500m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        // savingsRate = (income − expenses) / income = 500 / 1000 = 0.5
        data.SavingsRate.Should().BeApproximately(0.5m, 0.0001m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_IncludesNetWorthEntries()
    {
        // Asset account is active — at least one entry expected when balances exist.
        AddMtdTransaction(SalaryCategoryId, 500m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        // We cannot assert an exact count because other active accounts may exist,
        // but the list itself must be non-null.
        data.NetWorth.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_CountsPendingReminders()
    {
        // Create a reminder whose NextDueDate is today (always pending).
        var today = DateOnly.FromDateTime(DateTime.Today);
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Test Reminder",
            EstimatedAmount = 100m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = today,
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.PendingRemindersCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetDashboardDataAsync_DoesNotCountFutureReminders()
    {
        var countBefore = (await _service.GetDashboardDataAsync()).PendingRemindersCount;

        // Add a reminder due in the future — must not increase the pending count.
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Future Reminder",
            EstimatedAmount = 50m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = DateOnly.FromDateTime(DateTime.Today).AddMonths(1),
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var countAfter = (await _service.GetDashboardDataAsync()).PendingRemindersCount;

        countAfter.Should().Be(countBefore);
    }

    // -------------------------------------------------------------------------
    // GetHealthSnapshotAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetHealthSnapshotAsync_SpendableBalance_ExcludesExcludedAccountAndSubtractsDueRecurring()
    {
        // Non-excluded asset account (_accountId): balance 1000m
        AddMtdTransaction(SalaryCategoryId, 1000m);

        // Excluded asset account: balance 500m — must NOT count toward spendable
        var excludedAccount = new Account
        {
            Id                  = Guid.NewGuid(),
            Name                = $"Excluded Account {Guid.NewGuid():N}",
            AccountTypeId       = 1,
            CurrencyId          = 1,
            IsActive            = true,
            ExcludeFromSpendable = true
        };
        _fixture.Db.Accounts.Add(excludedAccount);
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1),
            Amount     = 500m,
            AccountId  = excludedAccount.Id,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });

        // Recurring transaction due this month (first day of current month) — should be subtracted
        var today = DateOnly.FromDateTime(DateTime.Today);
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Due This Month",
            EstimatedAmount = 200m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = new DateOnly(today.Year, today.Month, 1),
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // 1000 (non-excluded balance) - 200 (due recurring) = 800
        snapshot.SpendableBalance.Should().Be(800m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_SpendableBalance_NextMonthRecurringNotSubtracted()
    {
        // Asset account balance 1000m
        AddMtdTransaction(SalaryCategoryId, 1000m);

        // Recurring transaction due next month — must NOT be subtracted
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Due Next Month",
            EstimatedAmount = 200m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = DateOnly.FromDateTime(DateTime.Today).AddMonths(1),
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        snapshot.SpendableBalance.Should().Be(1000m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_Runway_ReturnsCorrectValue_GivenKnownExpensesAndNetWorth()
    {
        // Asset account (_accountId, CurrencyId=1): give it 6000m income this month
        AddMtdTransaction(SalaryCategoryId, 6000m);

        // Liability account: add 1000m housing expense → liability balance = 1000m
        var liabilityAccount = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Liability Account {Guid.NewGuid():N}",
            AccountTypeId = 2,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(liabilityAccount);
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1),
            Amount     = 1000m,
            AccountId  = liabilityAccount.Id,
            CategoryId = HousingCategoryId,
            CreatedAt  = DateTime.UtcNow
        });

        // 6 past-month Housing expense transactions (500m each) — directly inserted with past dates
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 1; i <= 6; i++)
        {
            var pastDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            _fixture.Db.Transactions.Add(new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = pastDate,
                Amount     = 500m,
                AccountId  = _accountId,
                CategoryId = HousingCategoryId,
                CreatedAt  = DateTime.UtcNow
            });
        }
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // Net worth = 6000 (asset) - 1000 (liability) = 5000
        // Avg monthly expense (last 6 months) = 500
        // Runway = 5000 / 500 = 10
        snapshot.RunwayMonths.Should().NotBeNull();
        snapshot.RunwayMonths!.Value.Should().BeApproximately(10m, 0.01m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_Runway_ReturnsNull_WhenNoExpensesInLast6Months()
    {
        // No past-month expense transactions seeded
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        snapshot.RunwayMonths.Should().BeNull();
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_IncomeDelta_ReturnsCorrectPercentage()
    {
        // 6 months of past income (1000m each) → rolling average = 1000
        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var i = 1; i <= 6; i++)
        {
            var pastDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            _fixture.Db.Transactions.Add(new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = pastDate,
                Amount     = 1000m,
                AccountId  = _accountId,
                CategoryId = SalaryCategoryId,
                CreatedAt  = DateTime.UtcNow
            });
        }

        // Current month income: 1200m
        AddMtdTransaction(SalaryCategoryId, 1200m);
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // deltaPercent = (1200 - 1000) / 1000 = 0.2
        snapshot.IncomeDeltaPercent.Should().NotBeNull();
        snapshot.IncomeDeltaPercent!.Value.Should().BeApproximately(0.2m, 0.001m);
        snapshot.CurrentMonthIncome.Should().BeGreaterThanOrEqualTo(1200m);
        snapshot.RollingAverageIncome.Should().NotBeNull();
        snapshot.RollingAverageIncome!.Value.Should().BeApproximately(1000m, 0.01m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_IncomeDelta_ReturnsNull_WhenNoPriorMonthIncome()
    {
        // Only current month income, no prior months
        AddMtdTransaction(SalaryCategoryId, 500m);
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        snapshot.IncomeDeltaPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_BudgetBurnRate_ReturnsCorrectRatio()
    {
        // Create an active CategoryBudget for Housing (CurrencyId=1, limit=1000)
        _fixture.Db.CategoryBudgets.Add(new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 1000m,
            IsActive    = true
        });

        // Spend 400m Housing this month on the EUR account
        AddMtdTransaction(HousingCategoryId, 400m);
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // BurnRate = 400 / 1000 = 0.4
        snapshot.BudgetBurnRate.Should().NotBeNull();
        snapshot.BudgetBurnRate!.Value.Should().BeApproximately(0.4m, 0.001m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_BudgetBurnRate_ReturnsNull_WhenNoActiveCategoryBudgets()
    {
        // Deactivate any existing active CategoryBudgets for CurrencyId=1
        var activeBudgets = _fixture.Db.CategoryBudgets
            .Where(b => b.IsActive && b.CurrencyId == 1)
            .ToList();
        foreach (var budget in activeBudgets)
            budget.IsActive = false;

        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        snapshot.BudgetBurnRate.Should().BeNull();
    }
}
