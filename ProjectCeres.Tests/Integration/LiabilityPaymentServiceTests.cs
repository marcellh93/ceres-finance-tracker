using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for LiabilityPaymentService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CurrencyId    2 = USD  (used for currency-mismatch validation tests)
/// </summary>
[Collection("IntegrationTests")]
public class LiabilityPaymentServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private LiabilityPaymentService _service = null!;
    private AccountService _accountService = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new SingleUserAccessor());
        _service = new LiabilityPaymentService(_fixture.Db, _accountService);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync(int currencyId = 1, DateOnly? openingBalanceDate = null) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Asset {Guid.NewGuid():N}",
            AccountTypeId      = 1,
            CurrencyId         = currencyId,
            OpeningBalance     = 0m,
            OpeningBalanceDate = openingBalanceDate ?? DateOnly.FromDateTime(DateTime.Today)
        })).Id;

    private async Task<Guid> CreateAssetAccountWithOpeningBalanceAsync(decimal amount, DateOnly date) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Asset {Guid.NewGuid():N}",
            AccountTypeId      = 1,
            CurrencyId         = 1,
            OpeningBalance     = amount,
            OpeningBalanceDate = date
        })).Id;

    private async Task<Guid> CreateLiabilityAccountAsync(int currencyId = 1, DateOnly? openingBalanceDate = null) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Liability {Guid.NewGuid():N}",
            AccountTypeId      = 2,
            CurrencyId         = currencyId,
            OpeningBalance     = 0m,
            OpeningBalanceDate = openingBalanceDate ?? DateOnly.FromDateTime(DateTime.Today)
        })).Id;

    private async Task<Guid> CreateLiabilityAccountWithOpeningBalanceAsync(decimal amount, DateOnly date) =>
        (await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Liability {Guid.NewGuid():N}",
            AccountTypeId      = 2,
            CurrencyId         = 1,
            OpeningBalance     = amount,
            OpeningBalanceDate = date
        })).Id;

    private TransactionCreateViewModel MakeCreateVm(Guid assetId, Guid liabilityId, decimal amount = 100m) =>
        new()
        {
            TransactionType    = "LiabilityPayment",
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = amount,
            Description        = "Test payment",
            AccountId          = assetId,
            LiabilityAccountId = liabilityId
        };

    // -------------------------------------------------------------------------
    // CreateAsync — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsPayment()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();

        var payment = await _service.CreateAsync(MakeCreateVm(assetId, liabilityId, amount: 250m));

        var reloaded = await _fixture.Db.LiabilityPayments.FindAsync(payment.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Amount.Should().Be(250m);
        reloaded.AssetAccountId.Should().Be(assetId);
        reloaded.LiabilityAccountId.Should().Be(liabilityId);
        reloaded.Description.Should().Be("Test payment");
    }

    // -------------------------------------------------------------------------
    // CreateAsync — validation guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ThrowsWhenPayingAccountIsNotAsset()
    {
        // Use a liability account as the "paying" account — must be rejected.
        var liabilityId1 = await CreateLiabilityAccountAsync();
        var liabilityId2 = await CreateLiabilityAccountAsync();

        var act = async () => await _service.CreateAsync(MakeCreateVm(liabilityId1, liabilityId2));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Asset account*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenReceivingAccountIsNotLiability()
    {
        // Use an asset account as the liability target — must be rejected.
        var assetId1 = await CreateAssetAccountAsync();
        var assetId2 = await CreateAssetAccountAsync();

        var act = async () => await _service.CreateAsync(MakeCreateVm(assetId1, assetId2));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Liability account*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCurrenciesDiffer()
    {
        // Asset is EUR (1), liability is USD (2) — must be rejected.
        var assetId     = await CreateAssetAccountAsync(currencyId: 1);
        var liabilityId = await CreateLiabilityAccountAsync(currencyId: 2);

        var act = async () => await _service.CreateAsync(MakeCreateVm(assetId, liabilityId));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same currency*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDateIsBeforeAssetOpeningBalance()
    {
        var openingDate = new DateOnly(2026, 3, 1);
        // Asset has an opening balance dated 2026-03-01; payment dated 2026-02-01 must be rejected.
        var assetId     = await CreateAssetAccountWithOpeningBalanceAsync(500m, openingDate);
        var liabilityId = await CreateLiabilityAccountAsync();

        var vm = MakeCreateVm(assetId, liabilityId);
        vm.Date = new DateOnly(2026, 2, 1);

        var act = async () => await _service.CreateAsync(vm);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opening balance date*");
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDateIsBeforeLiabilityOpeningBalance()
    {
        var openingDate = new DateOnly(2026, 3, 1);
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountWithOpeningBalanceAsync(200m, openingDate);

        var vm = MakeCreateVm(assetId, liabilityId);
        vm.Date = new DateOnly(2026, 2, 1);

        var act = async () => await _service.CreateAsync(vm);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opening balance date*");
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ChangesFields()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        var payment     = await _service.CreateAsync(MakeCreateVm(assetId, liabilityId, amount: 100m));

        await _service.UpdateAsync(new TransactionEditViewModel
        {
            Id                 = payment.Id,
            TransactionType    = "LiabilityPayment",
            Date               = DateOnly.FromDateTime(DateTime.Today),
            Amount             = 999m,
            Description        = "Updated payment",
            AccountId          = assetId,
            LiabilityAccountId = liabilityId
        });

        var reloaded = await _fixture.Db.LiabilityPayments.FindAsync(payment.Id);
        reloaded!.Amount.Should().Be(999m);
        reloaded.Description.Should().Be("Updated payment");
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesPayment()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        var payment     = await _service.CreateAsync(MakeCreateVm(assetId, liabilityId, amount: 75m));

        await _service.DeleteAsync(payment.Id);

        var reloaded = await _fixture.Db.LiabilityPayments.FindAsync(payment.Id);
        reloaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsPaymentWithNavigationProperties()
    {
        var assetId     = await CreateAssetAccountAsync();
        var liabilityId = await CreateLiabilityAccountAsync();
        var payment     = await _service.CreateAsync(MakeCreateVm(assetId, liabilityId, amount: 50m));

        var result = await _service.GetByIdAsync(payment.Id);

        result.Should().NotBeNull();
        result!.AssetAccount.Should().NotBeNull();
        result.LiabilityAccount.Should().NotBeNull();
        result.AssetAccount.Currency.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
