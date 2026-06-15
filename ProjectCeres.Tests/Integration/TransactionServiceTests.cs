using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
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
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD  (used for currency-mismatch tests)
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing     (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class TransactionServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private TransactionService _service = null!;
    private AccountService _accountService = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, _accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var attachmentService = new Mock<IFileAttachmentService>().Object;
        _service = new TransactionService(_fixture.Db, _accountService, liabilityPaymentService, attachmentService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

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
            TransactionType = "Regular",
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = amount,
            Description     = description,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            BudgetId        = null
        };

    private async Task<Guid> CreateAssetAccountAsync(string? name = null, int currencyId = 1) =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: name ?? $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: currencyId,
            Description: null,
            OpeningBalance: 0m,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private async Task<Guid> CreateLiabilityAccountAsync(string? name = null, int currencyId = 1) =>
        (await _accountService.TryCreateAsync(new CreateAccountRequest(
            Name: name ?? $"Liability {Guid.NewGuid():N}",
            AccountTypeId: 2,
            CurrencyId: currencyId,
            Description: null,
            OpeningBalance: 0m,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    // -------------------------------------------------------------------------
    // Regular transaction — CRUD
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsTransaction()
    {
        await _service.CreateAsync(CreateVm(amount: 150m, description: "Test transaction"));

        var saved = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId && t.Amount == 150m)
            .FirstOrDefaultAsync();

        saved.Should().NotBeNull();
        saved!.Description.Should().Be("Test transaction");
        saved.AccountId.Should().Be(_accountId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_PersistsIsClearedFromVm(bool isCleared)
    {
        var vm = CreateVm(amount: 150m, description: $"cleared={isCleared}");
        vm.IsCleared = isCleared;

        await _service.CreateAsync(vm);

        var saved = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId && t.Description == vm.Description)
            .FirstOrDefaultAsync();

        saved.Should().NotBeNull();
        saved!.IsCleared.Should().Be(isCleared);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsOnExistingTransaction()
    {
        // Insert directly so we have a known ID.
        var id = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = id,
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            Description = "Original",
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        await _service.UpdateAsync(new TransactionEditViewModel
        {
            Id              = id,
            TransactionType = "Regular",
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 200m,
            Description     = "Updated",
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            BudgetId        = null
        });

        var reloaded = await _fixture.Db.Transactions.FindAsync(id);
        reloaded!.Amount.Should().Be(200m);
        reloaded.Description.Should().Be("Updated");
        reloaded.CategoryId.Should().Be(HousingCategoryId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTransactionFromDatabase()
    {
        var id = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = id,
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 75m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        await _service.DeleteAsync(id);

        var reloaded = await _fixture.Db.Transactions.FindAsync(id);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_ExcludesSystemTransactions()
    {
        var openingBalanceCategoryId = new Guid("20000000-0000-0000-0000-000000000001");

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 500m,
            AccountId  = _accountId,
            CategoryId = openingBalanceCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
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

    // -------------------------------------------------------------------------
    // LiabilityPayment routing through TransactionService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_IncludesLiabilityPayments()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();

        // One regular transaction on the asset account.
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = assetId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });

        // One liability payment.
        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 200m,
            AssetAccountId     = assetId,
            LiabilityAccountId = liabilityId,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var results = (await _service.GetRecentAsync(accountId: assetId)).ToList();

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.TransactionType == "Regular" && r.Amount == 100m);
        results.Should().Contain(r => r.TransactionType == "LiabilityPayment" && r.Amount == 200m);
    }

    [Fact]
    public async Task CountAsync_IncludesLiabilityPayments()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = assetId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 50m,
            AssetAccountId     = assetId,
            LiabilityAccountId = liabilityId,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var count = await _service.CountAsync(accountId: assetId);

        count.Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_DeletesLiabilityPayment()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        var paymentId   = Guid.NewGuid();

        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = paymentId,
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 300m,
            AssetAccountId     = assetId,
            LiabilityAccountId = liabilityId,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        await _service.DeleteAsync(paymentId);

        var reloaded = await _fixture.Db.LiabilityPayments.FindAsync(paymentId);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdForEditAsync_ReturnsRegularTransaction()
    {
        var id = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = id,
            Date       = new DateOnly(2026, 3, 15),
            Amount     = 88m,
            Description = "Regular txn",
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var vm = await _service.GetByIdForEditAsync(id);

        vm.Should().NotBeNull();
        vm!.TransactionType.Should().Be("Regular");
        vm.Amount.Should().Be(88m);
        vm.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetByIdForEditAsync_ReturnsLiabilityPayment()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        var paymentId   = Guid.NewGuid();

        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = paymentId,
            Date               = new DateOnly(2026, 4, 1),
            Amount             = 450m,
            Description        = "Credit card payment",
            AssetAccountId     = assetId,
            LiabilityAccountId = liabilityId,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var vm = await _service.GetByIdForEditAsync(paymentId);

        vm.Should().NotBeNull();
        vm!.TransactionType.Should().Be("LiabilityPayment");
        vm.Amount.Should().Be(450m);
        vm.AccountId.Should().Be(assetId);
        vm.LiabilityAccountId.Should().Be(liabilityId);
    }

    [Fact]
    public async Task GetByIdForEditAsync_ReturnsNull_WhenNotFound()
    {
        var vm = await _service.GetByIdForEditAsync(Guid.NewGuid());
        vm.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Goal budget currency validation
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateSpendingBudgetAsync(int currencyId)
    {
        var budget = new Budget
        {
            Id           = Guid.NewGuid(),
            Name         = $"Spending Budget {Guid.NewGuid():N}",
            TargetAmount = 500m,
            CurrencyId   = currencyId,
            StartDate    = DateOnly.FromDateTime(DateTime.Today),
            GoalType     = "Spending",
            IsActive     = true
        };
        _fixture.Db.Budgets.Add(budget);
        await _fixture.Db.SaveChangesAsync();
        return budget.Id;
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenBudgetCurrencyMismatchesAccount()
    {
        var usdAccountId = await CreateAssetAccountAsync(currencyId: 2); // USD
        var eurBudgetId  = await CreateSpendingBudgetAsync(currencyId: 1); // EUR

        var vm = CreateVm();
        vm.AccountId = usdAccountId;
        vm.BudgetId  = eurBudgetId;

        var act = () => _service.CreateAsync(vm);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different currency*");
    }

    [Fact]
    public async Task CreateAsync_Succeeds_WhenBudgetCurrencyMatchesAccount()
    {
        var eurBudgetId = await CreateSpendingBudgetAsync(currencyId: 1); // EUR, same as _accountId

        var vm = CreateVm();
        vm.BudgetId = eurBudgetId;

        var act = () => _service.CreateAsync(vm);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenBudgetCurrencyMismatchesAccount()
    {
        var id = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = id,
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var usdAccountId = await CreateAssetAccountAsync(currencyId: 2); // USD
        var eurBudgetId  = await CreateSpendingBudgetAsync(currencyId: 1); // EUR

        var vm = new TransactionEditViewModel
        {
            Id              = id,
            TransactionType = "Regular",
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 100m,
            AccountId       = usdAccountId,
            CategoryId      = SalaryCategoryId,
            BudgetId        = eurBudgetId
        };

        var act = () => _service.UpdateAsync(vm);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different currency*");
    }

    [Fact]
    public async Task UpdateAsync_Succeeds_WhenBudgetCurrencyMatchesAccount()
    {
        var id = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = id,
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 100m,
            AccountId  = _accountId,
            CategoryId = SalaryCategoryId,
            CreatedAt  = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var eurBudgetId = await CreateSpendingBudgetAsync(currencyId: 1); // EUR, same as _accountId

        var vm = new TransactionEditViewModel
        {
            Id              = id,
            TransactionType = "Regular",
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 100m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            BudgetId        = eurBudgetId
        };

        var act = () => _service.UpdateAsync(vm);
        await act.Should().NotThrowAsync();
    }
}
