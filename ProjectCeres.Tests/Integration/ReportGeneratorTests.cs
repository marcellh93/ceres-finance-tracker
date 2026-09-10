using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for the four Stage 7 report generators against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing    (Expense, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000009 = Utilities  (Expense, non-system)
/// </summary>
[Collection("IntegrationParallel3")]
public class ReportGeneratorTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId    = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId   = new("20000000-0000-0000-0000-000000000008");
    private static readonly Guid UtilitiesCategoryId = new("20000000-0000-0000-0000-000000000009");

    private readonly TestDbFixture _fixture = new();
    private AccountService _accountService = null!;
    private CategoryBudgetService _categoryBudgetService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var sentinel = new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"));
        _accountService         = new AccountService(_fixture.Db, sentinel, TimeProvider.System);
        var settingsService     = new SettingsService(_fixture.Db, sentinel);
        _categoryBudgetService  = new CategoryBudgetService(_fixture.Db, settingsService, sentinel);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync(int currencyId = 1, decimal openingBalance = 0m) =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: currencyId,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private async Task<Guid> CreateLiabilityAccountAsync(int currencyId = 1, decimal openingBalance = 0m) =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: $"Liability {Guid.NewGuid():N}",
            AccountTypeId: 2,
            CurrencyId: currencyId,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private void AddTransaction(Guid accountId, Guid categoryId, decimal amount, DateOnly? date = null)
    {
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = date ?? DateOnly.FromDateTime(DateTime.Today),
            Amount     = amount,
            AccountId  = accountId,
            CategoryId = categoryId,
            CreatedAt  = DateTime.UtcNow
        });
    }

    private async Task<Guid> CreateCategoryBudgetAsync(Guid categoryId, int currencyId, decimal limit) =>
        (await _categoryBudgetService.CreateAsync(new CategoryBudgetCreateViewModel
        {
            CategoryId  = categoryId,
            CurrencyId  = currencyId,
            LimitAmount = limit
        })).Id;

    // -------------------------------------------------------------------------
    // BudgetVsActualReportGenerator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BudgetVsActual_ReturnsPlanVsActualPerCategory()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 500m);
        await CreateCategoryBudgetAsync(UtilitiesCategoryId, currencyId: 1, limit: 200m);

        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 1, 31);

        AddTransaction(accountId, HousingCategoryId,   300m, new DateOnly(2026, 1, 10));
        AddTransaction(accountId, UtilitiesCategoryId, 150m, new DateOnly(2026, 1, 15));
        await _fixture.Db.SaveChangesAsync();

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        result.Should().HaveCount(2);

        var housing = result.Single(r => r.CategoryName == "Housing / Rent");
        housing.LimitPerPeriod.Should().Be(500m);
        housing.TotalLimit.Should().Be(500m);
        housing.ActualSpend.Should().Be(300m);
        housing.Variance.Should().Be(200m);

        var utilities = result.Single(r => r.CategoryName == "Utilities");
        utilities.LimitPerPeriod.Should().Be(200m);
        utilities.TotalLimit.Should().Be(200m);
        utilities.ActualSpend.Should().Be(150m);
        utilities.Variance.Should().Be(50m);
    }

    [Fact]
    public async Task BudgetVsActual_ExcludesTransactionsOutsideDateRange()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 500m);

        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 1, 31);

        AddTransaction(accountId, HousingCategoryId, 300m, new DateOnly(2026, 1, 10));
        AddTransaction(accountId, HousingCategoryId, 999m, new DateOnly(2026, 2, 5));
        await _fixture.Db.SaveChangesAsync();

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        result.Single(r => r.CategoryName == "Housing / Rent").ActualSpend.Should().Be(300m);
    }

    [Fact]
    public async Task BudgetVsActual_ExcludesInactiveBudgets()
    {
        var accountId  = await CreateAssetAccountAsync(currencyId: 1);
        var budgetId   = await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 500m);
        await _categoryBudgetService.DeactivateAsync(budgetId);

        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 1, 31);

        AddTransaction(accountId, HousingCategoryId, 300m, new DateOnly(2026, 1, 10));
        await _fixture.Db.SaveChangesAsync();

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BudgetVsActual_BudgetWithNoSpend_ShowsZeroActual()
    {
        await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 500m);

        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 1, 31);

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        var row = result.Single();
        row.LimitPerPeriod.Should().Be(500m);
        row.TotalLimit.Should().Be(500m);
        row.ActualSpend.Should().Be(0m);
        row.Variance.Should().Be(500m);
    }

    [Fact]
    public async Task BudgetVsActual_ThreeMonthRange_MultipliesLimitByPeriodCount()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 200m);

        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 3, 31);

        AddTransaction(accountId, HousingCategoryId, 600m, new DateOnly(2026, 2, 10));
        await _fixture.Db.SaveChangesAsync();

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        var row = result.Single(r => r.CategoryName == "Housing / Rent");
        row.LimitPerPeriod.Should().Be(200m);
        row.TotalLimit.Should().Be(600m);   // 200 × 3 months
        row.ActualSpend.Should().Be(600m);
        row.Variance.Should().Be(0m);
    }

    [Fact]
    public async Task BudgetVsActual_SingleMonthRange_LimitUnchanged()
    {
        await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 300m);

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        var row = result.Single(r => r.CategoryName == "Housing / Rent");
        row.LimitPerPeriod.Should().Be(300m);
        row.TotalLimit.Should().Be(300m);
    }

    [Fact]
    public async Task BudgetVsActual_PeriodStartDay15_ThreePeriodsInRange()
    {
        // With PeriodStartDay=15, the period spanning Jan 15 – Feb 14 is named "February".
        // A range of Jan 15 to Apr 14 covers 3 full periods (Feb, Mar, Apr).
        // Stage 7: scope the Settings query to the sentinel UserId — Settings is
        // per-user post-cutover, and SingleAsync() would throw if any other test
        // had registered users and triggered SettingsService.GetAsync (which
        // lazily creates a Settings row on first read).
        var sentinelUserId = new Guid("00000000-0000-0000-0000-000000000001");
        var settings = await _fixture.Db.Settings.SingleAsync(s => s.UserId == sentinelUserId);
        settings.PeriodStartDay = 15;
        await _fixture.Db.SaveChangesAsync();

        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        await CreateCategoryBudgetAsync(HousingCategoryId, currencyId: 1, limit: 100m);

        var from = new DateOnly(2026, 1, 15);
        var to   = new DateOnly(2026, 4, 14);

        AddTransaction(accountId, HousingCategoryId, 300m, new DateOnly(2026, 2, 10));
        await _fixture.Db.SaveChangesAsync();

        var generator = new BudgetVsActualReportGenerator(_fixture.Db, new SettingsService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"))), new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<BudgetVsActualRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: from, To: to));

        var row = result.Single(r => r.CategoryName == "Housing / Rent");
        row.LimitPerPeriod.Should().Be(100m);
        row.TotalLimit.Should().Be(300m);   // 100 × 3 periods
        row.ActualSpend.Should().Be(300m);
        row.Variance.Should().Be(0m);
    }

    // -------------------------------------------------------------------------
    // LargestExpensesReportGenerator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LargestExpenses_ReturnsTopNExpensesOrderedByAmountDesc()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);

        AddTransaction(accountId, HousingCategoryId,   800m, new DateOnly(2026, 1, 5));
        AddTransaction(accountId, HousingCategoryId,   200m, new DateOnly(2026, 1, 10));
        AddTransaction(accountId, UtilitiesCategoryId, 500m, new DateOnly(2026, 1, 15));
        await _fixture.Db.SaveChangesAsync();

        var generator = new LargestExpensesReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<LargestExpenseRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31), Limit: 10));

        result.Should().HaveCount(3);
        result[0].Amount.Should().Be(800m);
        result[1].Amount.Should().Be(500m);
        result[2].Amount.Should().Be(200m);
    }

    [Fact]
    public async Task LargestExpenses_RespectsLimit()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        for (var i = 1; i <= 5; i++)
            AddTransaction(accountId, HousingCategoryId, i * 100m, new DateOnly(2026, 1, i));
        await _fixture.Db.SaveChangesAsync();

        var generator = new LargestExpensesReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<LargestExpenseRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31), Limit: 3));

        result.Should().HaveCount(3);
        result[0].Amount.Should().Be(500m);
    }

    [Fact]
    public async Task LargestExpenses_ExcludesIncome()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        AddTransaction(accountId, HousingCategoryId, 400m, new DateOnly(2026, 1, 5));
        AddTransaction(accountId, SalaryCategoryId,  3000m, new DateOnly(2026, 1, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new LargestExpensesReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<LargestExpenseRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(400m);
    }

    [Fact]
    public async Task LargestExpenses_ExcludesTransactionsOutsideDateRange()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        AddTransaction(accountId, HousingCategoryId, 400m, new DateOnly(2026, 1, 5));
        AddTransaction(accountId, HousingCategoryId, 999m, new DateOnly(2026, 2, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new LargestExpensesReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<LargestExpenseRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(400m);
    }

    // -------------------------------------------------------------------------
    // MonthlyCashFlowReportGenerator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MonthlyCashFlow_ReturnsIncomeAndExpensesGroupedByMonth()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);

        AddTransaction(accountId, SalaryCategoryId,  2000m, new DateOnly(2026, 1, 1));
        AddTransaction(accountId, HousingCategoryId,  800m, new DateOnly(2026, 1, 15));
        AddTransaction(accountId, SalaryCategoryId,  2100m, new DateOnly(2026, 2, 1));
        AddTransaction(accountId, HousingCategoryId,  900m, new DateOnly(2026, 2, 15));
        await _fixture.Db.SaveChangesAsync();

        var generator = new MonthlyCashFlowReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<MonthlyCashFlowRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 2, 28)));

        result.Should().HaveCount(2);

        var jan = result.Single(r => r.Year == 2026 && r.Month == 1);
        jan.TotalIncome.Should().Be(2000m);
        jan.TotalExpenses.Should().Be(800m);
        jan.Net.Should().Be(1200m);

        var feb = result.Single(r => r.Year == 2026 && r.Month == 2);
        feb.TotalIncome.Should().Be(2100m);
        feb.TotalExpenses.Should().Be(900m);
        feb.Net.Should().Be(1200m);
    }

    [Fact]
    public async Task MonthlyCashFlow_ExcludesSystemTransactions()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1, openingBalance: 5000m);

        AddTransaction(accountId, SalaryCategoryId, 2000m, new DateOnly(2026, 1, 5));
        await _fixture.Db.SaveChangesAsync();

        var generator = new MonthlyCashFlowReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<MonthlyCashFlowRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        result.Single().TotalIncome.Should().Be(2000m);
    }

    [Fact]
    public async Task MonthlyCashFlow_FiltersByCurrency()
    {
        var eurAccountId = await CreateAssetAccountAsync(currencyId: 1);
        var usdAccountId = await CreateAssetAccountAsync(currencyId: 2);

        AddTransaction(eurAccountId, SalaryCategoryId, 2000m, new DateOnly(2026, 1, 1));
        AddTransaction(usdAccountId, SalaryCategoryId, 9999m, new DateOnly(2026, 1, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new MonthlyCashFlowReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<MonthlyCashFlowRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        result.Single().TotalIncome.Should().Be(2000m);
    }

    [Fact]
    public async Task MonthlyCashFlow_MonthWithNoActivity_NotIncluded()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);
        AddTransaction(accountId, SalaryCategoryId, 2000m, new DateOnly(2026, 1, 1));
        AddTransaction(accountId, SalaryCategoryId, 2100m, new DateOnly(2026, 3, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new MonthlyCashFlowReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<MonthlyCashFlowRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 3, 31)));

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.Month == 2);
    }

    // -------------------------------------------------------------------------
    // NetWorthOverTimeReportGenerator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NetWorthOverTime_ReturnsMonthlyEquitySnapshots()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);

        AddTransaction(accountId, SalaryCategoryId,  2000m, new DateOnly(2026, 1, 15));
        AddTransaction(accountId, HousingCategoryId,  500m, new DateOnly(2026, 1, 20));
        AddTransaction(accountId, SalaryCategoryId,  1500m, new DateOnly(2026, 2, 10));
        await _fixture.Db.SaveChangesAsync();

        var generator = new NetWorthOverTimeReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<NetWorthSnapshotRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 2, 28)));

        result.Should().HaveCount(2);

        var jan = result.Single(r => r.Year == 2026 && r.Month == 1);
        jan.Assets.Should().Be(1500m);   // 2000 income - 500 expense
        jan.Liabilities.Should().Be(0m);
        jan.NetWorth.Should().Be(1500m);

        var feb = result.Single(r => r.Year == 2026 && r.Month == 2);
        feb.Assets.Should().Be(3000m);   // 1500 accumulated (jan) + 1500 more
        feb.NetWorth.Should().Be(3000m);
    }

    [Fact]
    public async Task NetWorthOverTime_IncludesLiabilityAccounts()
    {
        var assetId     = await CreateAssetAccountAsync(currencyId: 1);
        var liabilityId = await CreateLiabilityAccountAsync(currencyId: 1);

        AddTransaction(assetId,     SalaryCategoryId,  5000m, new DateOnly(2026, 1, 1));
        AddTransaction(liabilityId, HousingCategoryId, 2000m, new DateOnly(2026, 1, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new NetWorthOverTimeReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<NetWorthSnapshotRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        var jan = result.Single();
        jan.Assets.Should().Be(5000m);
        jan.Liabilities.Should().Be(2000m);
        jan.NetWorth.Should().Be(3000m);
    }

    [Fact]
    public async Task NetWorthOverTime_FiltersByCurrency()
    {
        var eurAccountId = await CreateAssetAccountAsync(currencyId: 1);
        var usdAccountId = await CreateAssetAccountAsync(currencyId: 2);

        AddTransaction(eurAccountId, SalaryCategoryId, 3000m, new DateOnly(2026, 1, 1));
        AddTransaction(usdAccountId, SalaryCategoryId, 9999m, new DateOnly(2026, 1, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new NetWorthOverTimeReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<NetWorthSnapshotRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 1, 31)));

        result.Single().Assets.Should().Be(3000m);
    }

    [Fact]
    public async Task NetWorthOverTime_SnapshotsAreCumulative()
    {
        var accountId = await CreateAssetAccountAsync(currencyId: 1);

        AddTransaction(accountId, SalaryCategoryId, 1000m, new DateOnly(2026, 1, 1));
        AddTransaction(accountId, SalaryCategoryId, 1000m, new DateOnly(2026, 2, 1));
        AddTransaction(accountId, SalaryCategoryId, 1000m, new DateOnly(2026, 3, 1));
        await _fixture.Db.SaveChangesAsync();

        var generator = new NetWorthOverTimeReportGenerator(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var result    = (List<NetWorthSnapshotRow>)await generator.GenerateAsync(
            new ReportParameters(CurrencyId: 1, From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 3, 31)));

        result.Should().HaveCount(3);
        result.Single(r => r.Month == 1).NetWorth.Should().Be(1000m);
        result.Single(r => r.Month == 2).NetWorth.Should().Be(2000m);
        result.Single(r => r.Month == 3).NetWorth.Should().Be(3000m);
    }
}
