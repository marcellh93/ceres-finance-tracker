using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for Goal Budget progress and validation in BudgetService.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   CurrencyId    1 = EUR
///   AccountTypeId 1 = Asset
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class GoalBudgetServiceTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private BudgetService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db);
        _service = new BudgetService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<Budget> CreateSpendingGoalAsync(string? name = null, decimal target = 3000m) =>
        _service.CreateAsync(new BudgetCreateViewModel
        {
            Name         = name ?? $"Spending Goal {Guid.NewGuid():N}",
            TargetAmount = target,
            CurrencyId   = 1,
            StartDate    = new DateOnly(2026, 1, 1),
            GoalType     = "Spending"
        });

    private Task<Budget> CreateSavingsGoalAsync(Guid linkedAccountId, string? name = null, decimal target = 5000m) =>
        _service.CreateAsync(new BudgetCreateViewModel
        {
            Name            = name ?? $"Savings Goal {Guid.NewGuid():N}",
            TargetAmount    = target,
            CurrencyId      = 1,
            StartDate       = new DateOnly(2026, 1, 1),
            GoalType        = "Savings",
            LinkedAccountId = linkedAccountId
        });

    private async Task<Guid> CreateAssetAccountAsync(decimal openingBalance = 0m) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Asset {Guid.NewGuid():N}",
            AccountTypeId      = 1,
            CurrencyId         = 1,
            OpeningBalance     = openingBalance,
            OpeningBalanceDate = new DateOnly(2026, 1, 1)
        })).Id;

    // -------------------------------------------------------------------------
    // GoalType validation on CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SavingsGoal_WithNullLinkedAccountId_Throws()
    {
        var act = async () => await _service.CreateAsync(new BudgetCreateViewModel
        {
            Name            = "Emergency Fund",
            TargetAmount    = 5000m,
            CurrencyId      = 1,
            StartDate       = new DateOnly(2026, 1, 1),
            GoalType        = "Savings",
            LinkedAccountId = null
        });

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*LinkedAccountId*");
    }

    [Fact]
    public async Task CreateAsync_SpendingGoal_WithLinkedAccountId_Throws()
    {
        var accountId = await CreateAssetAccountAsync();

        var act = async () => await _service.CreateAsync(new BudgetCreateViewModel
        {
            Name            = "Trip to Japan",
            TargetAmount    = 3000m,
            CurrencyId      = 1,
            StartDate       = new DateOnly(2026, 1, 1),
            GoalType        = "Spending",
            LinkedAccountId = accountId
        });

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*LinkedAccountId*");
    }

    // -------------------------------------------------------------------------
    // GetProgressAsync — Spending goal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProgressAsync_SpendingGoal_ReturnsTaggedTransactionSum()
    {
        var goal      = await CreateSpendingGoalAsync(target: 3000m);
        var accountId = await CreateAssetAccountAsync();

        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 2, 10),
                Amount     = 800m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = goal.Id,
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = new DateOnly(2026, 3, 5),
                Amount     = 600m,
                AccountId  = accountId,
                CategoryId = HousingCategoryId,
                BudgetId   = goal.Id,
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var progress = await _service.GetProgressAsync(goal.Id);

        progress.AmountProgress.Should().Be(1400m);
        progress.TargetAmount.Should().Be(3000m);
        progress.Remaining.Should().Be(1600m);
        progress.PercentUsed.Should().BeApproximately(46.67m, 0.01m);
    }

    [Fact]
    public async Task GetProgressAsync_SpendingGoal_ReturnsZeroWhenNoTransactionsTagged()
    {
        var goal = await CreateSpendingGoalAsync(target: 1000m);

        var progress = await _service.GetProgressAsync(goal.Id);

        progress.AmountProgress.Should().Be(0m);
        progress.Remaining.Should().Be(1000m);
        progress.PercentUsed.Should().Be(0m);
    }

    // -------------------------------------------------------------------------
    // GetProgressAsync — Savings goal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProgressAsync_SavingsGoal_ReturnsLinkedAccountBalance()
    {
        var accountId = await CreateAssetAccountAsync(openingBalance: 2500m);
        var goal      = await CreateSavingsGoalAsync(linkedAccountId: accountId, target: 5000m);

        var progress = await _service.GetProgressAsync(goal.Id);

        progress.AmountProgress.Should().Be(2500m);
        progress.TargetAmount.Should().Be(5000m);
        progress.Remaining.Should().Be(2500m);
        progress.PercentUsed.Should().Be(50m);
    }

    [Fact]
    public async Task GetProgressAsync_SavingsGoal_BalanceGrowsWithTransactions()
    {
        var accountId = await CreateAssetAccountAsync(openingBalance: 1000m);
        var goal      = await CreateSavingsGoalAsync(linkedAccountId: accountId, target: 5000m);

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(2026, 2, 1),
            Amount     = 500m,
            AccountId  = accountId,
            CategoryId = HousingCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var progress = await _service.GetProgressAsync(goal.Id);

        progress.AmountProgress.Should().Be(1500m);
    }
}
