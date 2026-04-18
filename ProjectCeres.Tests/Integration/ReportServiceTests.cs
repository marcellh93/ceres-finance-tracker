using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for ReportService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing     (Expense, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000009 = Utilities   (Expense, non-system)
/// </summary>
public class ReportServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId    = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId   = new("20000000-0000-0000-0000-000000000008");
    private static readonly Guid UtilitiesCategoryId = new("20000000-0000-0000-0000-000000000009");

    private readonly TestDbFixture _fixture = new();
    private ReportService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db);
        _service = new ReportService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync(int currencyId = 1, decimal openingBalance = 0m) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Asset {Guid.NewGuid():N}",
            AccountTypeId      = 1,
            CurrencyId         = currencyId,
            OpeningBalance     = openingBalance,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        })).Id;

    private async Task<Guid> CreateLiabilityAccountAsync(int currencyId = 1, decimal openingBalance = 0m) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Liability {Guid.NewGuid():N}",
            AccountTypeId      = 2,
            CurrencyId         = currencyId,
            OpeningBalance     = openingBalance,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        })).Id;

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

    // -------------------------------------------------------------------------
    // GetNetWorthAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetNetWorthAsync_ReturnsEntry_WithCorrectAssetAndLiabilityTotals()
    {
        var assetId     = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityId = await CreateLiabilityAccountAsync(openingBalance: 300m);
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetNetWorthAsync();

        var eur = result.First(e => e.CurrencyCode == "EUR");
        eur.Assets.Should().Be(1000m);
        eur.Liabilities.Should().Be(300m);
        eur.NetWorth.Should().Be(700m);
    }

    [Fact]
    public async Task GetNetWorthAsync_ExcludesInactiveAccounts()
    {
        var assetId = await CreateAssetAccountAsync(openingBalance: 500m);

        // Deactivate the account directly — inactive accounts must be excluded from net worth.
        var account = await _fixture.Db.Accounts.FindAsync(assetId);
        account!.IsActive = false;
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetNetWorthAsync();

        // Any EUR entry that exists should not include the deactivated account's balance.
        var eur = result.FirstOrDefault(e => e.CurrencyCode == "EUR");
        if (eur is not null)
            eur.Assets.Should().NotBe(500m);
    }

    [Fact]
    public async Task GetNetWorthAsync_GroupsByCurrency()
    {
        await CreateAssetAccountAsync(currencyId: 1, openingBalance: 800m);  // EUR
        await CreateAssetAccountAsync(currencyId: 2, openingBalance: 500m);  // USD
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetNetWorthAsync();

        result.Should().Contain(e => e.CurrencyCode == "EUR");
        result.Should().Contain(e => e.CurrencyCode == "USD");
    }

    // -------------------------------------------------------------------------
    // GetIncomeExpenseSummaryAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetIncomeExpenseSummaryAsync_ReturnsTotalsAndSavingsRate()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountId, SalaryCategoryId,  3000m, new DateOnly(2026, 3, 1));
        AddTransaction(accountId, HousingCategoryId, 1000m, new DateOnly(2026, 3, 15));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetIncomeExpenseSummaryAsync(currencyId: 1, from, to);

        result.TotalIncome.Should().Be(3000m);
        result.TotalExpenses.Should().Be(1000m);
        result.SavingsRate.Should().BeApproximately(2m / 3m, 0.0001m);
        result.CurrencyCode.Should().Be("EUR");
    }

    [Fact]
    public async Task GetIncomeExpenseSummaryAsync_ExcludesTransactionsOutsideDateRange()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 4, 1);
        var to   = new DateOnly(2026, 4, 30);

        AddTransaction(accountId, SalaryCategoryId, 2000m, new DateOnly(2026, 3, 31)); // before range
        AddTransaction(accountId, SalaryCategoryId, 1500m, new DateOnly(2026, 4, 15)); // inside range
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetIncomeExpenseSummaryAsync(currencyId: 1, from, to);

        result.TotalIncome.Should().Be(1500m);
    }

    [Fact]
    public async Task GetIncomeExpenseSummaryAsync_ExcludesTransactionsFromOtherCurrencies()
    {
        var eurAccountId = await CreateAssetAccountAsync(currencyId: 1);
        var usdAccountId = await CreateAssetAccountAsync(currencyId: 2);
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(eurAccountId, SalaryCategoryId, 1000m, new DateOnly(2026, 6, 1));
        AddTransaction(usdAccountId, SalaryCategoryId, 5000m, new DateOnly(2026, 6, 1));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetIncomeExpenseSummaryAsync(currencyId: 1, from, to);

        result.TotalIncome.Should().Be(1000m);
    }

    [Fact]
    public async Task GetIncomeExpenseSummaryAsync_SavingsRateIsZero_WhenNoIncome()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountId, HousingCategoryId, 500m, new DateOnly(2026, 6, 1));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetIncomeExpenseSummaryAsync(currencyId: 1, from, to);

        result.TotalIncome.Should().Be(0m);
        result.SavingsRate.Should().Be(0m);
    }

    // -------------------------------------------------------------------------
    // GetExpenseBreakdownAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetExpenseBreakdownAsync_GroupsExpensesByCategory()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountId, HousingCategoryId,   800m, new DateOnly(2026, 3, 1));
        AddTransaction(accountId, HousingCategoryId,   200m, new DateOnly(2026, 4, 1));
        AddTransaction(accountId, UtilitiesCategoryId, 150m, new DateOnly(2026, 3, 15));
        AddTransaction(accountId, SalaryCategoryId,   3000m, new DateOnly(2026, 3, 1)); // income — must be excluded
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetExpenseBreakdownAsync(currencyId: 1, from, to);

        result.Categories.Should().NotContain(c => c.CategoryName == "Salary");
        var housing = result.Categories.FirstOrDefault(c => c.CategoryName == "Housing / Rent");
        housing.Should().NotBeNull();
        housing!.Total.Should().Be(1000m);

        var utilities = result.Categories.FirstOrDefault(c => c.CategoryName == "Utilities");
        utilities.Should().NotBeNull();
        utilities!.Total.Should().Be(150m);
    }

    [Fact]
    public async Task GetExpenseBreakdownAsync_OrdersByCategoryTotalDescending()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountId, UtilitiesCategoryId, 50m,  new DateOnly(2026, 3, 1));
        AddTransaction(accountId, HousingCategoryId,  900m, new DateOnly(2026, 3, 1));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetExpenseBreakdownAsync(currencyId: 1, from, to);

        result.Categories.First().Total.Should().BeGreaterThan(result.Categories.Last().Total);
    }

    [Fact]
    public async Task GetExpenseBreakdownAsync_ReturnsEmptyList_WhenNoExpenses()
    {
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 1, 31);

        var result = await _service.GetExpenseBreakdownAsync(currencyId: 1, from, to);

        result.Categories.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetTransactionHistoryAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetTransactionHistoryAsync_ReturnsTransactions_InDateRange()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 4, 1);
        var to   = new DateOnly(2026, 4, 30);

        AddTransaction(accountId, SalaryCategoryId, 100m, new DateOnly(2026, 3, 31)); // before
        AddTransaction(accountId, SalaryCategoryId, 200m, new DateOnly(2026, 4, 15)); // inside
        AddTransaction(accountId, SalaryCategoryId, 300m, new DateOnly(2026, 5, 1));  // after
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to);

        result.Should().HaveCount(1);
        result[0].Amount.Should().Be(200m);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_FiltersOptionally_ByAccountId()
    {
        var accountA = await CreateAssetAccountAsync();
        var accountB = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountA, SalaryCategoryId, 500m, new DateOnly(2026, 6, 1));
        AddTransaction(accountB, SalaryCategoryId, 800m, new DateOnly(2026, 6, 1));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to, accountId: accountA);

        result.Should().HaveCount(1);
        result[0].AccountId.Should().Be(accountA);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_FiltersOptionally_ByCategoryId()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        AddTransaction(accountId, SalaryCategoryId,  1000m, new DateOnly(2026, 6, 1));
        AddTransaction(accountId, HousingCategoryId,  400m, new DateOnly(2026, 6, 2));
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to, categoryId: SalaryCategoryId);

        result.Should().HaveCount(1);
        result[0].CategoryId.Should().Be(SalaryCategoryId);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_ExcludesSystemTransactions()
    {
        var accountId = await CreateAssetAccountAsync(openingBalance: 500m); // creates system transaction
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        var result = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to, accountId: accountId);

        result.Should().NotContain(t => t.Category.IsSystem);
    }

    [Fact]
    public async Task GetTransactionHistoryAsync_RespectsLimitAndOffset()
    {
        var accountId = await CreateAssetAccountAsync();
        var from = new DateOnly(2026, 1, 1);
        var to   = new DateOnly(2026, 12, 31);

        for (var i = 0; i < 5; i++)
            AddTransaction(accountId, SalaryCategoryId, 100m * (i + 1), new DateOnly(2026, 4, i + 1));
        await _fixture.Db.SaveChangesAsync();

        var page1 = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to, limit: 2, offset: 0);
        var page2 = await _service.GetTransactionHistoryAsync(currencyId: 1, from, to, limit: 2, offset: 2);

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Select(t => t.Id).Should().NotIntersectWith(page2.Select(t => t.Id));
    }
}
