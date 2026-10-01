using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for AccountService.GetBalanceAsync/GetLedgerAsync against the
/// real project_ceres_test database. Each test rolls back its transaction — no test
/// data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary  (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("TestDbFixtureTests")]
public class AccountServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private AccountService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _service = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync(decimal openingBalance = 0m) =>
        (await _service.TryCreateAsync(new CreateAccountRequest(
            Name: $"Asset {Guid.NewGuid():N}",
            AccountTypeId: 1,
            CurrencyId: 1,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private async Task<Guid> CreateLiabilityAccountAsync(decimal openingBalance = 0m) =>
        (await _service.TryCreateAsync(new CreateAccountRequest(
            Name: $"Liability {Guid.NewGuid():N}",
            AccountTypeId: 2,
            CurrencyId: 1,
            Description: null,
            OpeningBalance: openingBalance,
            OpeningBalanceDate: DateOnly.FromDateTime(DateTime.Today),
            LiabilityRepaymentType: null,
            InterestRate: null,
            ExcludeFromSpendable: false))).Value!.Id;

    private void AddTransaction(Guid accountId, Guid categoryId, decimal amount)
    {
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, AccountId = accountId, CategoryId = categoryId, CreatedAt = DateTime.UtcNow
        });
    }

    private void AddTransfer(Guid sourceAccountId, Guid destAccountId, decimal amount)
    {
        _fixture.Db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, SourceAccountId = sourceAccountId, DestAccountId = destAccountId, CreatedAt = DateTime.UtcNow
        });
    }

    private void AddLiabilityPayment(Guid assetAccountId, Guid liabilityAccountId, decimal amount)
    {
        _fixture.Db.LiabilityPayments.Add(new LiabilityPayment
        {
            Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today),
            Amount = amount, AssetAccountId = assetAccountId, LiabilityAccountId = liabilityAccountId, CreatedAt = DateTime.UtcNow
        });
    }

    // -------------------------------------------------------------------------
    // GetBalanceAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBalanceAsync_SumsTransactionsBySignedCategory()
    {
        var accountId = await CreateAssetAccountAsync();
        AddTransaction(accountId, SalaryCategoryId, 2000m);
        AddTransaction(accountId, HousingCategoryId, 500m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(accountId)).Should().Be(1500m);
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityFundedEntirelyByATransfer_IncludesTheTransfer()
    {
        var assetId = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityId = await CreateLiabilityAccountAsync(openingBalance: 0m);
        AddTransfer(assetId, liabilityId, 608.03m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(liabilityId)).Should().Be(608.03m,
            "the transferred-in amount is the liability's ENTIRE balance — it must not be zero");
    }

    [Fact]
    public async Task GetBalanceAsync_LiabilityPayment_ReducesBothLegs()
    {
        var assetId = await CreateAssetAccountAsync(openingBalance: 1000m);
        var liabilityId = await CreateLiabilityAccountAsync(openingBalance: 500m);
        AddLiabilityPayment(assetId, liabilityId, 200m);
        await _fixture.Db.SaveChangesAsync();

        (await _service.GetBalanceAsync(assetId)).Should().Be(800m);
        (await _service.GetBalanceAsync(liabilityId)).Should().Be(300m);
    }

    [Fact]
    public async Task GetBalanceAsync_UnknownAccountId_ReturnsZero()
    {
        (await _service.GetBalanceAsync(Guid.NewGuid())).Should().Be(0m);
    }
}
