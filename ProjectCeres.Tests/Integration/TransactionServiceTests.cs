using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for TransactionService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing     (Expense, non-system)
/// </summary>
public class TransactionServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private TransactionService _service = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var accountService = new AccountService(_fixture.Db);
        _service = new TransactionService(_fixture.Db, accountService);

        // Create a reusable test account (Asset, EUR) with no opening balance so any date is valid.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Txn Test Account {Guid.NewGuid():N}",
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

    private TransactionCreateViewModel CreateVm(decimal amount = 100m, string? description = null) =>
        new()
        {
            Date        = DateOnly.FromDateTime(DateTime.Today),
            Amount      = amount,
            Description = description,
            AccountId   = _accountId,
            CategoryId  = SalaryCategoryId,
            BudgetId    = null
        };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsTransaction()
    {
        var txn = await _service.CreateAsync(CreateVm(amount: 150m, description: "Test transaction"));

        var reloaded = await _fixture.Db.Transactions.FindAsync(txn.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Amount.Should().Be(150m);
        reloaded.Description.Should().Be("Test transaction");
        reloaded.AccountId.Should().Be(_accountId);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsOnExistingTransaction()
    {
        var txn = await _service.CreateAsync(CreateVm(amount: 100m, description: "Original"));

        await _service.UpdateAsync(new TransactionEditViewModel
        {
            Id          = txn.Id,
            Date        = txn.Date,
            Amount      = 200m,
            Description = "Updated",
            AccountId   = _accountId,
            CategoryId  = HousingCategoryId,
            BudgetId    = null
        });

        var reloaded = await _fixture.Db.Transactions.FindAsync(txn.Id);
        reloaded!.Amount.Should().Be(200m);
        reloaded.Description.Should().Be("Updated");
        reloaded.CategoryId.Should().Be(HousingCategoryId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTransactionFromDatabase()
    {
        var txn = await _service.CreateAsync(CreateVm(amount: 75m));

        await _service.DeleteAsync(txn.Id);

        var reloaded = await _fixture.Db.Transactions.FindAsync(txn.Id);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_FiltersExcludesSystemTransactions()
    {
        // System transactions (Opening Balance) should be excluded from GetRecentAsync.
        var openingBalanceCategoryId = new Guid("20000000-0000-0000-0000-000000000001");

        // Insert a system transaction directly.
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 500m,
            AccountId  = _accountId,
            CategoryId = openingBalanceCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        // Insert a regular transaction.
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var results = await _service.GetRecentAsync(accountId: _accountId);

        results.Should().HaveCount(1);
        results.First().Amount.Should().Be(100m);
    }
}
