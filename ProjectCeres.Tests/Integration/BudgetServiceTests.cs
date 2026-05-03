using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for BudgetService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD
///   AccountTypeId 1 = Asset
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class BudgetServiceTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private BudgetService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new SingleUserAccessor());
        _service = new BudgetService(_fixture.Db, _accountService, new SingleUserAccessor());
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<Budget> CreateBudgetAsync(
        string? name = null,
        decimal targetAmount = 1000m,
        int currencyId = 1,
        DateOnly? startDate = null,
        DateOnly? endDate = null) =>
        _service.CreateAsync(new BudgetCreateViewModel
        {
            Name         = name ?? $"Budget {Guid.NewGuid():N}",
            TargetAmount = targetAmount,
            CurrencyId   = currencyId,
            StartDate    = startDate ?? DateOnly.FromDateTime(DateTime.Today),
            EndDate      = endDate,
            Description  = null
        });

    private async Task<Guid> CreateAssetAccountAsync() =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: 1,
            Description: null,
            OpeningBalance: 0m,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsBudget()
    {
        var start = new DateOnly(2026, 1, 1);
        var end   = new DateOnly(2026, 12, 31);

        var budget = await _service.CreateAsync(new BudgetCreateViewModel
        {
            Name         = "Annual Housing Budget",
            TargetAmount = 7200m,
            CurrencyId   = 1,
            StartDate    = start,
            EndDate      = end,
            Description  = "Year budget for housing"
        });

        var reloaded = await _fixture.Db.Budgets.FindAsync(budget.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Annual Housing Budget");
        reloaded.TargetAmount.Should().Be(7200m);
        reloaded.CurrencyId.Should().Be(1);
        reloaded.StartDate.Should().Be(start);
        reloaded.EndDate.Should().Be(end);
        reloaded.Description.Should().Be("Year budget for housing");
        reloaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithNoEndDate_Persists()
    {
        var budget = await CreateBudgetAsync(endDate: null);

        var reloaded = await _fixture.Db.Budgets.FindAsync(budget.Id);
        reloaded!.EndDate.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsBudgetWithCurrency()
    {
        var budget = await CreateBudgetAsync();

        var result = await _service.GetByIdAsync(budget.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(budget.Id);
        result.Currency.Should().NotBeNull();
        result.Currency.Code.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTaggedTransactions_ForSpendingGoal()
    {
        var budget    = await _service.CreateAsync(new BudgetCreateViewModel
        {
            Name         = "Vacation Fund",
            TargetAmount = 2000m,
            CurrencyId   = 1,
            StartDate    = DateOnly.FromDateTime(DateTime.Today),
            GoalType     = "Spending"
        });
        var accountId = await CreateAssetAccountAsync();

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 400m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget.Id,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 600m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget.Id,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var result = await _service.GetByIdAsync(budget.Id);

        result.Should().NotBeNull();
        result!.Transactions.Should().HaveCount(2);
        result.Transactions.Sum(t => t.Amount).Should().Be(1000m);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveBudgets_ByDefault()
    {
        var active   = await CreateBudgetAsync(name: "Active Budget");
        var inactive = await CreateBudgetAsync(name: "Inactive Budget");
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync()).ToList();

        results.Should().Contain(b => b.Id == active.Id);
        results.Should().NotContain(b => b.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactive_ReturnsBothActiveAndInactive()
    {
        var active   = await CreateBudgetAsync(name: "Active Budget");
        var inactive = await CreateBudgetAsync(name: "Inactive Budget");
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync(includeInactive: true)).ToList();

        results.Should().Contain(b => b.Id == active.Id);
        results.Should().Contain(b => b.Id == inactive.Id);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ChangesAllFields()
    {
        var budget = await CreateBudgetAsync(name: "Old Name", targetAmount: 500m, currencyId: 1);

        await _service.UpdateAsync(new BudgetEditViewModel
        {
            Id           = budget.Id,
            Name         = "New Name",
            TargetAmount = 999m,
            CurrencyId   = 2,
            StartDate    = new DateOnly(2026, 6, 1),
            EndDate      = new DateOnly(2026, 12, 31),
            Description  = "Updated description"
        });

        var reloaded = await _fixture.Db.Budgets.FindAsync(budget.Id);
        reloaded!.Name.Should().Be("New Name");
        reloaded.TargetAmount.Should().Be(999m);
        reloaded.CurrencyId.Should().Be(2);
        reloaded.StartDate.Should().Be(new DateOnly(2026, 6, 1));
        reloaded.EndDate.Should().Be(new DateOnly(2026, 12, 31));
        reloaded.Description.Should().Be("Updated description");
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.UpdateAsync(new BudgetEditViewModel
        {
            Id           = Guid.NewGuid(),
            Name         = "Ghost",
            TargetAmount = 100m,
            CurrencyId   = 1,
            StartDate    = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // DeactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var budget = await CreateBudgetAsync();

        await _service.DeactivateAsync(budget.Id);

        var reloaded = await _fixture.Db.Budgets.FindAsync(budget.Id);
        reloaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DeactivateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // GetActualSpendAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetActualSpendAsync_ReturnsZero_WhenNoTransactionsLinked()
    {
        var budget = await CreateBudgetAsync();

        var actual = await _service.GetActualSpendAsync(budget.Id);

        actual.Should().Be(0m);
    }

    [Fact]
    public async Task GetActualSpendAsync_SumsTransactionsLinkedToBudget()
    {
        var budget    = await CreateBudgetAsync();
        var accountId = await CreateAssetAccountAsync();

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 300m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget.Id,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 150m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget.Id,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var actual = await _service.GetActualSpendAsync(budget.Id);

        actual.Should().Be(450m);
    }

    [Fact]
    public async Task GetActualSpendAsync_OnlySumsTransactionsForThatBudget()
    {
        var budget1   = await CreateBudgetAsync(name: "Budget 1");
        var budget2   = await CreateBudgetAsync(name: "Budget 2");
        var accountId = await CreateAssetAccountAsync();

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 200m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget1.Id,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 500m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = budget2.Id,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var actual1 = await _service.GetActualSpendAsync(budget1.Id);
        var actual2 = await _service.GetActualSpendAsync(budget2.Id);

        actual1.Should().Be(200m);
        actual2.Should().Be(500m);
    }
}
