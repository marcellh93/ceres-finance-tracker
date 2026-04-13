using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for AccountService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing     (Expense, non-system)
/// </summary>
public class AccountServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private AccountService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new AccountService(_fixture.Db);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Task<Account> CreateAssetAccountAsync(decimal openingBalance = 0m) =>
        _service.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Test Account {Guid.NewGuid():N}",
            AccountTypeId      = 1,  // Asset
            CurrencyId         = 1,  // EUR
            Description        = null,
            OpeningBalance     = openingBalance,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        });

    private Task<Account> CreateLiabilityAccountAsync(decimal openingBalance = 0m) =>
        _service.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Test Credit Card {Guid.NewGuid():N}",
            AccountTypeId      = 2,  // Liability
            CurrencyId         = 1,  // EUR
            Description        = null,
            OpeningBalance     = openingBalance,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        });

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithNonZeroOpeningBalance_AutoCreatesOpeningBalanceTransaction()
    {
        var account = await CreateAssetAccountAsync(openingBalance: 500m);

        var transactions = _fixture.Db.Transactions
            .Where(t => t.AccountId == account.Id)
            .ToList();

        transactions.Should().HaveCount(1);
        transactions[0].Amount.Should().Be(500m);
        transactions[0].Description.Should().Be("Opening Balance");
    }

    [Fact]
    public async Task CreateAsync_WithZeroOpeningBalance_DoesNotCreateTransaction()
    {
        var account = await CreateAssetAccountAsync(openingBalance: 0m);

        var count = _fixture.Db.Transactions.Count(t => t.AccountId == account.Id);
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetBalanceAsync_DerivesIncomeMinusExpenses()
    {
        // Create an account with no opening balance so balance starts at 0.
        var account = await CreateAssetAccountAsync(openingBalance: 0m);

        // Insert transactions directly — income increases, expense decreases.
        _fixture.Db.Transactions.AddRange(
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 2000m,
                AccountId  = account.Id,
                CategoryId = SalaryCategoryId,  // Income
                CreatedAt  = DateTime.UtcNow
            },
            new Transaction
            {
                Id         = Guid.NewGuid(),
                Date       = DateOnly.FromDateTime(DateTime.Today),
                Amount     = 600m,
                AccountId  = account.Id,
                CategoryId = HousingCategoryId, // Expense
                CreatedAt  = DateTime.UtcNow
            }
        );
        await _fixture.Db.SaveChangesAsync();

        var balance = await _service.GetBalanceAsync(account.Id);

        balance.Should().Be(1400m); // 2000 - 600
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityAccount_ExpenseIncreasesBalance()
    {
        // Charging an expense to a credit card must increase (not decrease) the balance.
        var account = await CreateLiabilityAccountAsync(openingBalance: 0m);

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 300m,
            AccountId  = account.Id,
            CategoryId = HousingCategoryId, // Expense
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var balance = await _service.GetBalanceAsync(account.Id);

        balance.Should().Be(300m); // owe 300, not -300
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var account = await CreateAssetAccountAsync();

        await _service.DeactivateAsync(account.Id);

        var reloaded = await _fixture.Db.Accounts.FindAsync(account.Id);
        reloaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ChangingOpeningBalance_UpdatesExistingTransaction()
    {
        var account = await CreateAssetAccountAsync(openingBalance: 100m);

        await _service.UpdateAsync(new AccountEditViewModel
        {
            Id                 = account.Id,
            Name               = account.Name,
            Description        = account.Description,
            OpeningBalance     = 999m,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var ob = await _service.GetOpeningBalanceAsync(account.Id);
        ob.Should().Be(999m);
    }

    [Fact]
    public async Task UpdateAsync_SettingOpeningBalanceToZero_RemovesTransaction()
    {
        var account = await CreateAssetAccountAsync(openingBalance: 100m);

        await _service.UpdateAsync(new AccountEditViewModel
        {
            Id                 = account.Id,
            Name               = account.Name,
            Description        = account.Description,
            OpeningBalance     = 0m,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today)
        });

        var ob = await _service.GetOpeningBalanceAsync(account.Id);
        ob.Should().Be(0m);
    }
}
