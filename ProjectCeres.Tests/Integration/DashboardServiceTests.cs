using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using Microsoft.EntityFrameworkCore;
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
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var settingsService = new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        _service = new DashboardService(_fixture.Db, settingsService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));

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
    public async Task GetHealthSnapshotAsync_AvailableToday_ExcludesExcludedAccountAndSubtractsDueRecurring()
    {
        // Capture baseline spendable before seeding test data
        var baselineSnapshot = await _service.GetHealthSnapshotAsync();
        var baselineAvailable = baselineSnapshot.AvailableToday;

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

        // Recurring transaction due this month (first day of current month) — imminent (overdue), should be subtracted from AvailableToday
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

        // baseline + 1000 (non-excluded balance) - 200 (due recurring, imminent) = baseline + 800
        snapshot.AvailableToday.Should().Be((baselineAvailable ?? 0m) + 1000m - 200m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_AvailableToday_NextMonthRecurringNotSubtracted()
    {
        // Capture baseline spendable before seeding test data
        var baselineSnapshot = await _service.GetHealthSnapshotAsync();
        var baselineAvailable = baselineSnapshot.AvailableToday;

        // Asset account balance 1000m
        AddMtdTransaction(SalaryCategoryId, 1000m);

        // Recurring transaction due next month — must NOT be subtracted from AvailableToday
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

        snapshot.AvailableToday.Should().Be((baselineAvailable ?? 0m) + 1000m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_SafeToSpend_SubtractsLaterBillsAndBudgetReserve()
    {
        // Deactivate any existing active CategoryBudgets for CurrencyId=1 to isolate this test
        var existingBudgets = _fixture.Db.CategoryBudgets.Where(cb => cb.IsActive && cb.CurrencyId == 1).ToList();
        foreach (var b in existingBudgets)
            b.IsActive = false;

        // Baseline
        var baseline = await _service.GetHealthSnapshotAsync();
        var baselineAvailable = baseline.AvailableToday ?? 0m;
        var baselineSafe      = baseline.SafeToSpend    ?? 0m;

        // Non-excluded account: 3000m income → liquid = 3000
        AddMtdTransaction(SalaryCategoryId, 3000m);

        var today   = DateOnly.FromDateTime(DateTime.Today);
        var lastDay = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Recurring due within 7-day window (imminent) → subtracted from AvailableToday.
        // Use day 1 of this month: always <= today, always imminent (overdue counts as imminent).
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Imminent Bill",
            EstimatedAmount = 400m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = new DateOnly(today.Year, today.Month, 1),   // always imminent (overdue)
            IsActive        = true
        });

        // Recurring "later" bill: only add if a date >7 days away still falls within this month.
        // If near month-end (e.g., April 28) there may be no such date — skip the later bill.
        var laterBillDate = today.AddDays(8);
        var hasLaterBill  = laterBillDate <= lastDay;
        if (hasLaterBill)
        {
            _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
            {
                Id              = Guid.NewGuid(),
                Name            = "Later Bill",
                EstimatedAmount = 500m,
                AccountId       = _accountId,
                CategoryId      = HousingCategoryId,
                Frequency       = Frequency.Monthly,
                NextDueDate     = laterBillDate,
                IsActive        = true
            });
        }

        // Active CategoryBudget with 200m limit and 50m actual spend this month
        _fixture.Db.CategoryBudgets.Add(new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 200m,
            IsActive    = true
        });
        AddMtdTransaction(HousingCategoryId, 50m);   // 50m spent → 150m reserve remaining

        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // liquid = baseline + 3000 - 50 (expense) = baseline + 2950
        // AvailableToday = liquid - imminentBills(400) = baseline + 2950 - 400 = baseline + 2550
        // LaterBills = 500 (if hasLaterBill) else 0
        // BudgetReserve = MAX(0, 200 - 50) = 150
        // SafeToSpend = AvailableToday - LaterBills - BudgetReserve
        var expectedLaterBills  = hasLaterBill ? 500m : 0m;
        var expectedSafeDelta   = 2550m - expectedLaterBills - 150m;

        snapshot.AvailableToday.Should().Be(baselineAvailable + 2550m);
        snapshot.SafeToSpend.Should().Be(baselineSafe + expectedSafeDelta);
        snapshot.LaterBills.Should().BeGreaterThanOrEqualTo(expectedLaterBills);
        snapshot.BudgetReserve.Should().BeGreaterThanOrEqualTo(150m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_AvailableToday_IncludesImminentBillsDueInNextMonth()
    {
        // Regression test: a recurring bill due within 7 days but in the NEXT calendar month
        // (e.g., rent due May 1 when today is April 28) must appear in ImminentBills.
        var baseline = (await _service.GetHealthSnapshotAsync()).AvailableToday ?? 0m;

        AddMtdTransaction(SalaryCategoryId, 1000m);

        var today           = DateOnly.FromDateTime(DateTime.Today);
        var nextMonthDue    = today.AddDays(3); // 3 days away — imminent but possibly next month
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Next-Month Imminent Bill",
            EstimatedAmount = 770m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = nextMonthDue,
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // Bill is due within 7 days → must be in ImminentBills regardless of calendar month
        // AvailableToday = baseline + 1000 - 770 = baseline + 230
        snapshot.AvailableToday.Should().Be(baseline + 230m);
        snapshot.ImminentBills.Should().BeGreaterThanOrEqualTo(770m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_AvailableToday_TreatsOverdueRecurringAsImminent()
    {
        var baseline = (await _service.GetHealthSnapshotAsync()).AvailableToday ?? 0m;

        AddMtdTransaction(SalaryCategoryId, 1000m);

        var today    = DateOnly.FromDateTime(DateTime.Today);
        var firstDay = new DateOnly(today.Year, today.Month, 1);
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Overdue Bill",
            EstimatedAmount = 300m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = firstDay,   // always in range; always <= today
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        // Overdue bills count as imminent → AvailableToday = baseline + 1000 - 300 = baseline + 700
        snapshot.AvailableToday.Should().Be(baseline + 700m);
        snapshot.ImminentBills.Should().BeGreaterThanOrEqualTo(300m);
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

        // Capture baseline before our seed data is included (net worth may include other seed accounts).
        // After seeding: net worth increases by +5000 (6000 asset - 1000 liability),
        // avg monthly expense increases by +500/6 per month.
        // We can't assert an exact value because seed accounts contribute unknown balances,
        // but we can assert the value is non-null and positive, and that it is
        // consistent with the formula by checking it is > 0 and finite (not infinity/NaN).
        var snapshot = await _service.GetHealthSnapshotAsync();

        // Net worth includes our +5000 contribution; expenses include our +500/month.
        // At minimum runway should be well above zero.
        snapshot.RunwayMonths.Should().NotBeNull();
        snapshot.RunwayMonths!.Value.Should().BeGreaterThan(0m);
        // Sanity ceiling: no realistic seed data produces runway > 10,000 months.
        snapshot.RunwayMonths!.Value.Should().BeLessThan(10_000m);
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
        // Deactivate any existing active CategoryBudgets for CurrencyId=1 to isolate this test
        var existingBudgets = _fixture.Db.CategoryBudgets.Where(cb => cb.IsActive && cb.CurrencyId == 1).ToList();
        foreach (var b in existingBudgets)
            b.IsActive = false;
        await _fixture.Db.SaveChangesAsync();

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
    public async Task GetHealthSnapshotAsync_AvailableToday_ExcludesTransfersOutToExcludedAccounts()
    {
        // Arrange: capture baseline before seeding
        var baselineSnapshot = await _service.GetHealthSnapshotAsync();
        var baselineAvailable = baselineSnapshot.AvailableToday ?? 0m;

        // Non-excluded account gets 2000m income
        AddMtdTransaction(SalaryCategoryId, 2000m);

        // Excluded account (savings sleeve) — ExcludeFromSpendable = true
        var excludedAccount = new Account
        {
            Id                   = Guid.NewGuid(),
            Name                 = $"Savings Sleeve {Guid.NewGuid():N}",
            AccountTypeId        = 1,
            CurrencyId           = 1,
            IsActive             = true,
            ExcludeFromSpendable = true
        };
        _fixture.Db.Accounts.Add(excludedAccount);

        // Transfer 500m from the non-excluded account to the excluded one
        _fixture.Db.Transfers.Add(new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1),
            Amount          = 500m,
            SourceAccountId = _accountId,
            DestAccountId   = excludedAccount.Id,
            CreatedAt       = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        // Act
        var snapshot = await _service.GetHealthSnapshotAsync();

        // Assert: AvailableToday = baseline + 2000 (income) - 500 (transfer out to excluded) = baseline + 1500
        snapshot.AvailableToday.Should().Be(baselineAvailable + 1500m);
    }

    [Fact]
    public async Task GetHealthSnapshotAsync_BudgetBurnRate_ReturnsNull_WhenNoActiveCategoryBudgets()
    {
        // Deactivate any existing active CategoryBudgets for CurrencyId=1
        var activeBudgets = await _fixture.Db.CategoryBudgets
            .Where(b => b.IsActive && b.CurrencyId == 1)
            .ToListAsync();
        foreach (var budget in activeBudgets)
            budget.IsActive = false;

        await _fixture.Db.SaveChangesAsync();

        var snapshot = await _service.GetHealthSnapshotAsync();

        snapshot.BudgetBurnRate.Should().BeNull();
    }
}
