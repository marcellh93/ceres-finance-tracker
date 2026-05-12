using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using Xunit;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for CategoryBudgetService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing    (Expense, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000009 = Groceries  (Expense, non-system)
///   AccountTypeId 1 = Asset
/// </summary>
[Collection("IntegrationTests")]
public class CategoryBudgetServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");
    private static readonly Guid GroceriesCategoryId = new("20000000-0000-0000-0000-000000000009");

    private readonly TestDbFixture _fixture = new();
    private CategoryBudgetService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var sentinel = new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001"));
        _accountService = new AccountService(_fixture.Db, sentinel);
        var settingsService = new SettingsService(_fixture.Db, sentinel);
        _service = new CategoryBudgetService(_fixture.Db, settingsService, sentinel);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<CategoryBudget> CreateBudgetAsync(
        Guid? categoryId = null,
        int currencyId = 1,
        decimal limitAmount = 300m) =>
        _service.CreateAsync(new CategoryBudgetCreateViewModel
        {
            CategoryId  = categoryId ?? HousingCategoryId,
            CurrencyId  = currencyId,
            LimitAmount = limitAmount
        });

    private async Task<Guid> CreateAssetAccountAsync(int currencyId = 1) =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: currencyId,
            Description: null,
            OpeningBalance: 0m,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    // -------------------------------------------------------------------------
    // CreateAsync — expense-only guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_OnIncomeCategory_Throws()
    {
        var act = async () => await CreateBudgetAsync(categoryId: SalaryCategoryId);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Expense*");
    }

    // -------------------------------------------------------------------------
    // CreateAsync — duplicate active guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WhenActiveBudgetAlreadyExistsForSameCategoryAndCurrency_Throws()
    {
        var existing = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);

        var act = async () => await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);

        await act.Should()
            .ThrowAsync<DuplicateBudgetException>()
            .WithMessage("*category and currency already exists*")
            .Where(e => e.ExistingBudgetId == existing.Id && e.ExistingIsActive);
    }

    [Fact]
    public async Task CreateAsync_WhenActiveBudgetExistsForSameCategoryDifferentCurrency_Succeeds()
    {
        await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);

        var act = async () => await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 2);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAsync_WhenInactiveBudgetExistsForSameCategoryAndCurrency_Throws()
    {
        var existing = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        await _service.DeactivateAsync(existing.Id);

        var act = async () => await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);

        await act.Should()
            .ThrowAsync<DuplicateBudgetException>()
            .Where(e => e.ExistingBudgetId == existing.Id && !e.ExistingIsActive);
    }

    [Fact]
    public async Task CreateAsync_ValidCategoryBudget_Persists()
    {
        var budget = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1, limitAmount: 500m);

        var reloaded = await _fixture.Db.CategoryBudgets.FindAsync(budget.Id);
        reloaded.Should().NotBeNull();
        reloaded!.CategoryId.Should().Be(HousingCategoryId);
        reloaded.CurrencyId.Should().Be(1);
        reloaded.LimitAmount.Should().Be(500m);
        reloaded.IsActive.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // GetActualSpendAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetActualSpendAsync_ReturnsZero_WhenNoTransactionsInMonth()
    {
        var budget    = await CreateBudgetAsync();
        var accountId = await CreateAssetAccountAsync();

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(2025, 1, 15),
            Amount     = 200m,
            AccountId  = accountId,
            CategoryId = HousingCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var actual = await _service.GetActualSpendAsync(budget.Id, 2026, 1);

        actual.Should().Be(0m);
    }

    [Fact]
    public async Task GetActualSpendAsync_SumsTransactionsInSpecifiedMonth()
    {
        var budget    = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        var accountId = await CreateAssetAccountAsync(currencyId: 1);

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 3, 5),
                Amount     = 400m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 3, 20),
                Amount     = 150m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 4, 1),
                Amount     = 999m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var actual = await _service.GetActualSpendAsync(budget.Id, 2026, 3);

        actual.Should().Be(550m);
    }

    [Fact]
    public async Task GetActualSpendAsync_OnlySumsMatchingCategoryAndCurrency()
    {
        var housingBudget  = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        var groceriesBudget = await CreateBudgetAsync(categoryId: GroceriesCategoryId, currencyId: 1);
        var accountId      = await CreateAssetAccountAsync(currencyId: 1);

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 3, 10),
                Amount     = 300m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 3, 15),
                Amount     = 120m,
                AccountId  = accountId,
                CategoryId = GroceriesCategoryId,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var housingActual  = await _service.GetActualSpendAsync(housingBudget.Id, 2026, 3);
        var groceriesActual = await _service.GetActualSpendAsync(groceriesBudget.Id, 2026, 3);

        housingActual.Should().Be(300m);
        groceriesActual.Should().Be(120m);
    }

    // -------------------------------------------------------------------------
    // DeactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var budget = await CreateBudgetAsync();

        await _service.DeactivateAsync(budget.Id);

        var reloaded = await _fixture.Db.CategoryBudgets.FindAsync(budget.Id);
        reloaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DeactivateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ReactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReactivateAsync_SetsIsActiveTrue()
    {
        var budget = await CreateBudgetAsync();
        await _service.DeactivateAsync(budget.Id);

        await _service.ReactivateAsync(budget.Id);

        var reloaded = await _fixture.Db.CategoryBudgets.FindAsync(budget.Id);
        reloaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ReactivateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.ReactivateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReactivateAsync_ReturnsEarlyWhenAlreadyActive()
    {
        var budget = await CreateBudgetAsync();

        await _service.ReactivateAsync(budget.Id);

        var reloaded = await _fixture.Db.CategoryBudgets.FindAsync(budget.Id);
        reloaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ReactivateAsync_ThrowsDuplicateBudgetException_WhenAnotherActiveBudgetExistsForSameCategoryAndCurrency()
    {
        var first = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        await _service.DeactivateAsync(first.Id);

        // Create a different active budget for the same (Housing, EUR) category/currency
        // We use a separate test budget that hasn't been deactivated
        var another = await _fixture.Db.CategoryBudgets.AddAsync(new CategoryBudget
        {
            Id = Guid.NewGuid(),
            CategoryId = HousingCategoryId,
            CurrencyId = 1,
            LimitAmount = 200m,
            IsActive = true
        });
        await _fixture.Db.SaveChangesAsync();

        var act = async () => await _service.ReactivateAsync(first.Id);

        await act.Should()
            .ThrowAsync<DuplicateBudgetException>()
            .Where(e => e.ExistingBudgetId == another.Entity.Id && e.ExistingIsActive);
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveBudgets_ByDefault()
    {
        var active   = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        var inactive = await CreateBudgetAsync(categoryId: GroceriesCategoryId, currencyId: 1);
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync()).ToList();

        results.Should().Contain(b => b.Id == active.Id);
        results.Should().NotContain(b => b.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactive_ReturnsBoth()
    {
        var active   = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        var inactive = await CreateBudgetAsync(categoryId: GroceriesCategoryId, currencyId: 1);
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync(includeInactive: true)).ToList();

        results.Should().Contain(b => b.Id == active.Id);
        results.Should().Contain(b => b.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllAsync_Currency_FiltersOnCurrencyCode()
    {
        var eurBudget = await CreateBudgetAsync(categoryId: HousingCategoryId, currencyId: 1);
        var usdBudget = await CreateBudgetAsync(categoryId: GroceriesCategoryId, currencyId: 2);

        var eurResults = (await _service.GetAllAsync(currency: "EUR")).ToList();
        var usdResults = (await _service.GetAllAsync(currency: "USD")).ToList();

        eurResults.Should().Contain(b => b.Id == eurBudget.Id);
        eurResults.Should().NotContain(b => b.Id == usdBudget.Id);
        usdResults.Should().Contain(b => b.Id == usdBudget.Id);
        usdResults.Should().NotContain(b => b.Id == eurBudget.Id);
    }
}
