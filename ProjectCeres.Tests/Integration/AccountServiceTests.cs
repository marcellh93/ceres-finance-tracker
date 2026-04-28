using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using Microsoft.EntityFrameworkCore;

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
[Collection("IntegrationTests")]
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

    [Fact]
    public async Task GetBalanceAsync_LiabilityPayment_ReducesAssetBalance()
    {
        // Asset account starts with €1000. After a €300 liability payment, balance is €700.
        var assetAccount     = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityAccount = await CreateLiabilityAccountAsync();

        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 300m,
            AssetAccountId     = assetAccount.Id,
            LiabilityAccountId = liabilityAccount.Id,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var balance = await _service.GetBalanceAsync(assetAccount.Id);

        balance.Should().Be(700m);
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityPayment_ReducesLiabilityBalance()
    {
        // Liability account starts with €500 owed. After a €200 payment, balance is €300.
        var assetAccount     = await CreateAssetAccountAsync();
        var liabilityAccount = await CreateLiabilityAccountAsync(openingBalance: 500m);

        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 200m,
            AssetAccountId     = assetAccount.Id,
            LiabilityAccountId = liabilityAccount.Id,
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var balance = await _service.GetBalanceAsync(liabilityAccount.Id);

        balance.Should().Be(300m);
    }

    // -------------------------------------------------------------------------
    // Stage 5.1 — Repayment type / interest rate validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_AmortisingLiability_WithNullInterestRate_Throws()
    {
        var act = () => _service.CreateAsync(new AccountCreateViewModel
        {
            Name                   = $"Amortising {Guid.NewGuid():N}",
            AccountTypeId          = 2,  // Liability
            CurrencyId             = 1,  // EUR
            LiabilityRepaymentType = "Amortising",
            InterestRate           = null,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*interest rate*");
    }

    [Fact]
    public async Task CreateAsync_FullMonthlyLiability_WithInterestRate_Throws()
    {
        var act = () => _service.CreateAsync(new AccountCreateViewModel
        {
            Name                   = $"FullMonthly {Guid.NewGuid():N}",
            AccountTypeId          = 2,  // Liability
            CurrencyId             = 1,  // EUR
            LiabilityRepaymentType = "FullMonthly",
            InterestRate           = 0.15m,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*interest rate*");
    }

    [Fact]
    public async Task CreateAsync_AmortisingLiability_WithValidInterestRate_Succeeds()
    {
        var account = await _service.CreateAsync(new AccountCreateViewModel
        {
            Name                   = $"Mortgage {Guid.NewGuid():N}",
            AccountTypeId          = 2,  // Liability
            CurrencyId             = 1,  // EUR
            LiabilityRepaymentType = "Amortising",
            InterestRate           = 0.0350m,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        account.LiabilityRepaymentType.Should().Be("Amortising");
        account.InterestRate.Should().Be(0.0350m);
    }

    [Fact]
    public async Task UpdateAsync_AmortisingLiability_WithNullInterestRate_Throws()
    {
        var account = await _service.CreateAsync(new AccountCreateViewModel
        {
            Name                   = $"Mortgage {Guid.NewGuid():N}",
            AccountTypeId          = 2,
            CurrencyId             = 1,
            LiabilityRepaymentType = "Amortising",
            InterestRate           = 0.0350m,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        var act = () => _service.UpdateAsync(new AccountEditViewModel
        {
            Id                     = account.Id,
            Name                   = account.Name,
            LiabilityRepaymentType = "Amortising",
            InterestRate           = null,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*interest rate*");
    }

    [Fact]
    public async Task UpdateAsync_FullMonthlyLiability_WithInterestRate_Throws()
    {
        var account = await _service.CreateAsync(new AccountCreateViewModel
        {
            Name                   = $"Credit Card {Guid.NewGuid():N}",
            AccountTypeId          = 2,
            CurrencyId             = 1,
            LiabilityRepaymentType = "FullMonthly",
            InterestRate           = null,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        var act = () => _service.UpdateAsync(new AccountEditViewModel
        {
            Id                     = account.Id,
            Name                   = account.Name,
            LiabilityRepaymentType = "FullMonthly",
            InterestRate           = 0.20m,
            OpeningBalance         = 0m,
            OpeningBalanceDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*interest rate*");
    }

    // -------------------------------------------------------------------------
    // Stage 9 — Health metrics / ExcludeFromSpendable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_AssetAccount_WithExcludeFromSpendable_True_PersistedAsTrue()
    {
        var account = await _service.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Test Account {Guid.NewGuid():N}",
            AccountTypeId      = 1,  // Asset
            CurrencyId         = 1,  // EUR
            Description        = null,
            OpeningBalance     = 0m,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today),
            ExcludeFromSpendable = true
        });

        var reloaded = await _fixture.Db.Accounts.FindAsync(account.Id);
        reloaded!.ExcludeFromSpendable.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_LiabilityAccount_WithExcludeFromSpendable_True_ForcedToFalse()
    {
        var account = await _service.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Test Credit Card {Guid.NewGuid():N}",
            AccountTypeId      = 2,  // Liability
            CurrencyId         = 1,  // EUR
            Description        = null,
            OpeningBalance     = 0m,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today),
            ExcludeFromSpendable = true
        });

        var reloaded = await _fixture.Db.Accounts.FindAsync(account.Id);
        reloaded!.ExcludeFromSpendable.Should().BeFalse();
    }
}
