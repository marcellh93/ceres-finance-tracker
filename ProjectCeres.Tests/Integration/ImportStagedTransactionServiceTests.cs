using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using Moq;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for ImportStagedTransactionService.
///
/// Seed data:
///   AccountTypeId 1 = Asset
///   CategoryId 20000000-0000-0000-0000-000000000026 = Uncategorized Expense
/// </summary>
[Collection("IntegrationTests")]
public class ImportStagedTransactionServiceTests : IAsyncLifetime
{
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    private readonly TestDbFixture _fixture = new();
    private ImportStagedTransactionService _service = null!;
    private ITransactionService _transactionService = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        var accountService          = new AccountService(_fixture.Db, new SingleUserAccessor());
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService, new SingleUserAccessor());
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        _transactionService         = new TransactionService(_fixture.Db, accountService, liabilityPaymentService, attachmentService, new SingleUserAccessor());

        _service = new ImportStagedTransactionService(_fixture.Db, _transactionService, new SingleUserAccessor());

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Test Account {Guid.NewGuid():N}",
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

    private async Task<Guid> CreateTransactionAsync(DateOnly date, decimal amount)
    {
        return await _transactionService.CreateAsync(new TransactionCreateViewModel
        {
            Date        = date,
            Amount      = amount,
            Description = "Test transaction",
            AccountId   = _accountId,
            CategoryId  = UncategorizedExpenseId
        });
    }

    private async Task<ImportStagedTransaction> CreateStagedAsync(
        Guid matchedTransactionId,
        StagedTransactionStatus status = StagedTransactionStatus.Pending)
    {
        var staged = new ImportStagedTransaction
        {
            Id                   = Guid.NewGuid(),
            ImportedAt           = DateTime.UtcNow,
            AccountId            = _accountId,
            RawDate              = DateOnly.FromDateTime(DateTime.Today),
            RawAmount            = 100m,
            RawDescription       = "CSV row description",
            MatchedTransactionId = matchedTransactionId,
            Status               = status
        };
        _fixture.Db.ImportStagedTransactions.Add(staged);
        await _fixture.Db.SaveChangesAsync();
        return staged;
    }

    // -------------------------------------------------------------------------
    // GetPendingAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPendingRows()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);
        var txId3 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 300m);

        var pending   = await CreateStagedAsync(txId1, StagedTransactionStatus.Pending);
        var confirmed = await CreateStagedAsync(txId2, StagedTransactionStatus.Confirmed);
        var disputed  = await CreateStagedAsync(txId3, StagedTransactionStatus.Disputed);

        var result = await _service.GetPendingAsync();

        result.Should().ContainSingle(s => s.Id == pending.Id);
        result.Should().NotContain(s => s.Id == confirmed.Id);
        result.Should().NotContain(s => s.Id == disputed.Id);
    }

    [Fact]
    public async Task GetPendingAsync_IncludesAccountAndMatchedTransaction()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await CreateStagedAsync(txId);

        var result = await _service.GetPendingAsync();

        result.Should().ContainSingle();
        result[0].Account.Should().NotBeNull();
        result[0].MatchedTransaction.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // GetPendingCountAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPendingCountAsync_CountsOnlyPendingRows()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);

        await CreateStagedAsync(txId1, StagedTransactionStatus.Pending);
        await CreateStagedAsync(txId2, StagedTransactionStatus.Confirmed);

        var count = await _service.GetPendingCountAsync();

        count.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // TryConfirmAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryConfirmAsync_DoesNotChangeMatchedTransactionClearedState()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        var result = await _service.TryConfirmAsync(staged.Id);

        result.IsSuccess.Should().BeTrue();
        var tx = await _fixture.Db.Transactions.FindAsync(txId);
        tx!.IsCleared.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // TryConfirmAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryConfirmAllAsync_ConfirmsAllPendingRows()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);
        var txId3 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 300m);

        var staged1 = await CreateStagedAsync(txId1, StagedTransactionStatus.Pending);
        var staged2 = await CreateStagedAsync(txId2, StagedTransactionStatus.Pending);
        var staged3 = await CreateStagedAsync(txId3, StagedTransactionStatus.Pending);

        var result = await _service.TryConfirmAllAsync();

        result.IsSuccess.Should().BeTrue();

        var r1 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged1.Id);
        var r2 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged2.Id);
        var r3 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged3.Id);

        r1!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r2!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r3!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r1.ResolvedAt.Should().NotBeNull();
        r2.ResolvedAt.Should().NotBeNull();
        r3.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryConfirmAllAsync_IsIdempotentOnEmptyQueue()
    {
        // No staged rows seeded.
        var result = await _service.TryConfirmAllAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryConfirmAllAsync_SetsResolvedAtOnEveryConfirmedRow()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);

        var staged1 = await CreateStagedAsync(txId1, StagedTransactionStatus.Pending);
        var staged2 = await CreateStagedAsync(txId2, StagedTransactionStatus.Pending);

        var before = DateTime.UtcNow;
        var result = await _service.TryConfirmAllAsync();
        var after  = DateTime.UtcNow;

        result.IsSuccess.Should().BeTrue();

        var r1 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged1.Id);
        var r2 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged2.Id);

        r1!.ResolvedAt.Should().NotBeNull();
        r2!.ResolvedAt.Should().NotBeNull();
        r1.ResolvedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        r2.ResolvedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task TryConfirmAllAsync_DoesNotTouchAlreadyResolvedRows()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);
        var txId3 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 300m);

        var fixedResolvedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var alreadyConfirmed = await CreateStagedAsync(txId1, StagedTransactionStatus.Confirmed);
        alreadyConfirmed.ResolvedAt = fixedResolvedAt;

        var alreadyDisputed = await CreateStagedAsync(txId2, StagedTransactionStatus.Disputed);
        alreadyDisputed.ResolvedAt = fixedResolvedAt;

        await _fixture.Db.SaveChangesAsync();

        var pending = await CreateStagedAsync(txId3, StagedTransactionStatus.Pending);

        var result = await _service.TryConfirmAllAsync();

        result.IsSuccess.Should().BeTrue();

        var rConfirmed = await _fixture.Db.ImportStagedTransactions.FindAsync(alreadyConfirmed.Id);
        var rDisputed  = await _fixture.Db.ImportStagedTransactions.FindAsync(alreadyDisputed.Id);
        var rPending   = await _fixture.Db.ImportStagedTransactions.FindAsync(pending.Id);

        // Already-resolved rows must be untouched.
        rConfirmed!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        rConfirmed.ResolvedAt.Should().Be(fixedResolvedAt);

        rDisputed!.Status.Should().Be(StagedTransactionStatus.Disputed);
        rDisputed.ResolvedAt.Should().Be(fixedResolvedAt);

        // Pending row must now be Confirmed with a fresh ResolvedAt.
        rPending!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        rPending.ResolvedAt.Should().NotBeNull();
        rPending.ResolvedAt.Should().NotBe(fixedResolvedAt);
    }

    [Fact]
    public async Task TryConfirmAllAsync_DoesNotConfirmOtherUsersPendingRows()
    {
        // Own user (sentinel) seeds a Pending row through the standard helper.
        var ownTxId    = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var ownStaged  = await CreateStagedAsync(ownTxId, StagedTransactionStatus.Pending);

        // Sanity: the helper stamped UserId to the sentinel via the SaveChanges interceptor.
        ownStaged.UserId.Should().Be(SingleUserAccessor.SentinelUserId);

        // Intruder row: same MatchedTransactionId FK (FK is to Transactions, not user-scoped),
        // but stamped to a different user. Must bypass the helper to override the auto-stamp.
        var intruderUserId = Guid.NewGuid();
        var intruderStaged = new ImportStagedTransaction
        {
            Id                   = Guid.NewGuid(),
            UserId               = intruderUserId,
            ImportedAt           = DateTime.UtcNow,
            AccountId            = _accountId,
            RawDate              = DateOnly.FromDateTime(DateTime.Today),
            RawAmount            = 100m,
            RawDescription       = "Intruder CSV row",
            MatchedTransactionId = ownTxId,
            Status               = StagedTransactionStatus.Pending
        };
        _fixture.Db.ImportStagedTransactions.Add(intruderStaged);
        await _fixture.Db.SaveChangesAsync();

        // Re-load to confirm the auto-stamp interceptor did NOT overwrite the intruder UserId.
        var intruderReloadedBefore = await _fixture.Db.ImportStagedTransactions.FindAsync(intruderStaged.Id);
        intruderReloadedBefore!.UserId.Should().Be(intruderUserId,
            "test setup requires the intruder row to remain stamped to a foreign user");

        var result = await _service.TryConfirmAllAsync();

        result.IsSuccess.Should().BeTrue();

        var ownReloaded      = await _fixture.Db.ImportStagedTransactions.FindAsync(ownStaged.Id);
        var intruderReloaded = await _fixture.Db.ImportStagedTransactions.FindAsync(intruderStaged.Id);

        ownReloaded!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        ownReloaded.ResolvedAt.Should().NotBeNull();

        intruderReloaded!.Status.Should().Be(StagedTransactionStatus.Pending);
        intruderReloaded.ResolvedAt.Should().BeNull();
        intruderReloaded.UserId.Should().Be(intruderUserId);
    }

    // -------------------------------------------------------------------------
    // TryDisputeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryDisputeAsync_UnclearsTheOriginalTransaction()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        var result = await _service.TryDisputeAsync(staged.Id);

        result.IsSuccess.Should().BeTrue();
        var tx = await _fixture.Db.Transactions.FindAsync(txId);
        tx!.IsCleared.Should().BeFalse();
    }

    [Fact]
    public async Task TryDisputeAsync_InsertsNewTransactionWithNeedsReview()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        var countBefore = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);

        var result = await _service.TryDisputeAsync(staged.Id);

        result.IsSuccess.Should().BeTrue();
        var countAfter = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);
        countAfter.Should().Be(countBefore + 1);

        var newTx = _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId)
            .OrderByDescending(t => t.CreatedAt)
            .First();
        newTx.NeedsReview.Should().BeTrue();
        newTx.Amount.Should().Be(staged.RawAmount);
        newTx.Description.Should().Be(staged.RawDescription);
    }

    [Fact]
    public async Task TryDisputeAsync_WhenMatchedTransactionIsNull_StillInsertsNewTransaction()
    {
        var staged = new ImportStagedTransaction
        {
            Id                   = Guid.NewGuid(),
            ImportedAt           = DateTime.UtcNow,
            AccountId            = _accountId,
            RawDate              = DateOnly.FromDateTime(DateTime.Today),
            RawAmount            = 75m,
            RawDescription       = "Orphaned CSV row",
            MatchedTransactionId = null,
            Status               = StagedTransactionStatus.Pending
        };
        _fixture.Db.ImportStagedTransactions.Add(staged);
        await _fixture.Db.SaveChangesAsync();

        var countBefore = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);

        var result = await _service.TryDisputeAsync(staged.Id);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _fixture.Db.ImportStagedTransactions.FindAsync(staged.Id);
        reloaded!.Status.Should().Be(StagedTransactionStatus.Disputed);

        var countAfter = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);
        countAfter.Should().Be(countBefore + 1);
    }
}
