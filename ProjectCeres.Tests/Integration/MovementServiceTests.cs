using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for MovementService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   AccountTypeId 2 = Liability
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary     (Income)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing     (Expense)
/// </summary>
[Collection("IntegrationTests")]
public class MovementServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private IMovementService _service = null!;
    private AccountService _accountService = null!;

    private Guid _assetAccountId;
    private Guid _secondAssetAccountId;
    private Guid _liabilityAccountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        _service = new MovementService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));

        _assetAccountId = await CreateAssetAccountAsync();
        _secondAssetAccountId = await CreateAssetAccountAsync();
        _liabilityAccountId = await CreateLiabilityAccountAsync();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateAssetAccountAsync()
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Asset {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> CreateLiabilityAccountAsync()
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Liability {Guid.NewGuid():N}",
            AccountTypeId = 2,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<Guid> SeedTransactionAsync(Guid accountId, DateOnly date, decimal amount = 100m, DateTime? createdAt = null)
    {
        var txn = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = date,
            Amount     = amount,
            AccountId  = accountId,
            CategoryId = HousingCategoryId,
            IsCleared  = false,
            CreatedAt  = createdAt ?? DateTime.UtcNow
        };
        _fixture.Db.Transactions.Add(txn);
        await _fixture.Db.SaveChangesAsync();
        return txn.Id;
    }

    private async Task<Guid> SeedTransferAsync(Guid sourceId, Guid destId, DateOnly date, decimal amount = 50m, DateTime? createdAt = null)
    {
        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = date,
            Amount          = amount,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            IsCleared       = false,
            CreatedAt       = createdAt ?? DateTime.UtcNow
        };
        _fixture.Db.Transfers.Add(transfer);
        await _fixture.Db.SaveChangesAsync();
        return transfer.Id;
    }

    private async Task<Guid> SeedLiabilityPaymentAsync(Guid assetId, Guid liabilityId, DateOnly date, decimal amount = 75m, DateTime? createdAt = null)
    {
        var payment = new LiabilityPayment
        {
            Id                = Guid.NewGuid(),
            Date              = date,
            Amount            = amount,
            AssetAccountId    = assetId,
            LiabilityAccountId = liabilityId,
            IsCleared         = false,
            CreatedAt         = createdAt ?? DateTime.UtcNow
        };
        _fixture.Db.LiabilityPayments.Add(payment);
        await _fixture.Db.SaveChangesAsync();
        return payment.Id;
    }

    // -------------------------------------------------------------------------
    // GetRecentAsync — all three types interleaved, sorted Date DESC CreatedAt DESC
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_ReturnsAllThreeTypes_SortedByDateDescCreatedAtDesc()
    {
        var baseTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedTransactionAsync(_assetAccountId,      new DateOnly(2025, 6, 3), createdAt: baseTime.AddSeconds(1));
        await SeedTransferAsync(_assetAccountId, _secondAssetAccountId, new DateOnly(2025, 6, 2), createdAt: baseTime.AddSeconds(2));
        await SeedLiabilityPaymentAsync(_assetAccountId, _liabilityAccountId, new DateOnly(2025, 6, 1), createdAt: baseTime.AddSeconds(3));

        var results = await _service.GetRecentAsync(limit: 10, offset: 0);

        results.Should().HaveCount(3);
        results[0].MovementType.Should().Be(MovementType.Transaction);
        results[1].MovementType.Should().Be(MovementType.Transfer);
        results[2].MovementType.Should().Be(MovementType.LiabilityPayment);
    }

    [Fact]
    public async Task GetRecentAsync_SameDateDifferentCreatedAt_SortedByCreatedAtDesc()
    {
        var baseTime = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var sameDate = new DateOnly(2025, 6, 1);

        await SeedTransactionAsync(_assetAccountId, sameDate, createdAt: baseTime.AddSeconds(1));
        await SeedTransferAsync(_assetAccountId, _secondAssetAccountId, sameDate, createdAt: baseTime.AddSeconds(3));
        await SeedLiabilityPaymentAsync(_assetAccountId, _liabilityAccountId, sameDate, createdAt: baseTime.AddSeconds(2));

        var results = await _service.GetRecentAsync(limit: 10, offset: 0);

        results.Should().HaveCount(3);
        results[0].MovementType.Should().Be(MovementType.Transfer);
        results[1].MovementType.Should().Be(MovementType.LiabilityPayment);
        results[2].MovementType.Should().Be(MovementType.Transaction);
    }

    // -------------------------------------------------------------------------
    // GetRecentAsync — accountId filter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_WithAccountIdFilter_ReturnsOnlyRowsInvolvingThatAccount()
    {
        var date = new DateOnly(2025, 6, 1);

        // Belongs to _assetAccountId
        await SeedTransactionAsync(_assetAccountId, date);
        // Transfer from _assetAccountId — should appear
        await SeedTransferAsync(_assetAccountId, _secondAssetAccountId, date);
        // Transaction on _secondAssetAccountId only — should NOT appear
        await SeedTransactionAsync(_secondAssetAccountId, date);

        var results = await _service.GetRecentAsync(accountId: _assetAccountId, limit: 10, offset: 0);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r =>
            (r.AccountId == _assetAccountId ||
             r.SourceAccountId == _assetAccountId ||
             r.DestAccountId == _assetAccountId ||
             r.AssetAccountId == _assetAccountId).Should().BeTrue()
        );
    }

    // -------------------------------------------------------------------------
    // GetRecentAsync — date range filter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_WithDateRangeFilter_ReturnsOnlyRowsWithinRange()
    {
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 5, 31));  // before range
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 6, 1));   // on from — included
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 6, 15));  // in range — included
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 6, 30));  // on to — included
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 7, 1));   // after range

        var results = await _service.GetRecentAsync(
            from: new DateOnly(2025, 6, 1),
            to: new DateOnly(2025, 6, 30),
            limit: 10, offset: 0);

        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Date.Should().BeOnOrAfter(new DateOnly(2025, 6, 1)));
        results.Should().AllSatisfy(r => r.Date.Should().BeOnOrBefore(new DateOnly(2025, 6, 30)));
    }

    // -------------------------------------------------------------------------
    // CountAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountAsync_NoFilter_ReturnsTotalAcrossAllThreeTypes()
    {
        var date = new DateOnly(2025, 6, 1);

        await SeedTransactionAsync(_assetAccountId, date);
        await SeedTransferAsync(_assetAccountId, _secondAssetAccountId, date);
        await SeedLiabilityPaymentAsync(_assetAccountId, _liabilityAccountId, date);

        var count = await _service.CountAsync();

        count.Should().Be(3);
    }

    [Fact]
    public async Task CountAsync_WithDateRange_ReturnsCorrectCount()
    {
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 5, 31));
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 6, 15));
        await SeedTransferAsync(_assetAccountId, _secondAssetAccountId, new DateOnly(2025, 6, 20));
        await SeedTransactionAsync(_assetAccountId, new DateOnly(2025, 7, 1));

        var count = await _service.CountAsync(
            from: new DateOnly(2025, 6, 1),
            to: new DateOnly(2025, 6, 30));

        count.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Sync seed helpers for q-filter tests
    // -------------------------------------------------------------------------

    private Account SeedAssetAccount(AppDbContext db, string name)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        return account;
    }

    private void SeedTransaction(AppDbContext db, Guid accountId, Guid categoryId, decimal amount, string description)
    {
        var txn = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount      = amount,
            AccountId   = accountId,
            CategoryId  = categoryId,
            Description = description,
            IsCleared   = false,
            CreatedAt   = DateTime.UtcNow
        };
        db.Transactions.Add(txn);
    }

    // -------------------------------------------------------------------------
    // GetRecentAsync — q text-search filter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecent_FiltersByDescriptionOrCategoryName_WhenQProvided()
    {
        var db = _fixture.Db;
        var svc = _service;

        var account = SeedAssetAccount(db, "Checking");
        SeedTransaction(db, account.Id, SalaryCategoryId, amount: 100, description: "March salary");
        SeedTransaction(db, account.Id, HousingCategoryId, amount: 800, description: "Rent payment");
        SeedTransaction(db, account.Id, SalaryCategoryId, amount: 200, description: "Bonus");
        await db.SaveChangesAsync();

        // Match by description
        var rentResults = await svc.GetRecentAsync(q: "rent");
        rentResults.Should().HaveCount(1);
        rentResults[0].Description.Should().Be("Rent payment");

        // Match by category name (Salary category)
        var salaryResults = await svc.GetRecentAsync(q: "salary");
        salaryResults.Should().HaveCount(2);

        // Empty q returns all
        var allResults = await svc.GetRecentAsync(q: "");
        allResults.Should().HaveCount(3);

        // Null q returns all
        var nullResults = await svc.GetRecentAsync(q: null);
        nullResults.Should().HaveCount(3);
    }

    [Fact]
    public async Task Count_RespectsQFilter()
    {
        var db = _fixture.Db;
        var svc = _service;

        var account = SeedAssetAccount(db, "Checking");
        SeedTransaction(db, account.Id, SalaryCategoryId, amount: 100, description: "March salary");
        SeedTransaction(db, account.Id, HousingCategoryId, amount: 800, description: "Rent payment");
        await db.SaveChangesAsync();

        (await svc.CountAsync(q: "rent")).Should().Be(1);
        (await svc.CountAsync(q: null)).Should().Be(2);
    }
}
