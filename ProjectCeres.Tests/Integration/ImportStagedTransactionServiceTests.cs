using FluentAssertions;
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

        var accountService          = new AccountService(_fixture.Db);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService);
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        _transactionService         = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentService);

        _service = new ImportStagedTransactionService(_fixture.Db, _transactionService);

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
    // ConfirmAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_SetsStatusConfirmedAndResolvedAt()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        await _service.ConfirmAsync(staged.Id);

        var reloaded = await _fixture.Db.ImportStagedTransactions.FindAsync(staged.Id);
        reloaded!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        reloaded.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmAsync_DoesNotChangeMatchedTransactionClearedState()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        await _service.ConfirmAsync(staged.Id);

        var tx = await _fixture.Db.Transactions.FindAsync(txId);
        tx!.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.ConfirmAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ConfirmAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAllAsync_ConfirmsAllPendingRows()
    {
        var txId1 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var txId2 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 200m);
        var txId3 = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 300m);

        var staged1 = await CreateStagedAsync(txId1, StagedTransactionStatus.Pending);
        var staged2 = await CreateStagedAsync(txId2, StagedTransactionStatus.Pending);
        var staged3 = await CreateStagedAsync(txId3, StagedTransactionStatus.Confirmed);

        await _service.ConfirmAllAsync();

        var r1 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged1.Id);
        var r2 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged2.Id);
        var r3 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged3.Id);

        r1!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r2!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r3!.ResolvedAt.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // DisputeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisputeAsync_SetsStatusDisputed()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        await _service.DisputeAsync(staged.Id);

        var reloaded = await _fixture.Db.ImportStagedTransactions.FindAsync(staged.Id);
        reloaded!.Status.Should().Be(StagedTransactionStatus.Disputed);
        reloaded.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DisputeAsync_UnclearsTheOriginalTransaction()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        await _service.DisputeAsync(staged.Id);

        var tx = await _fixture.Db.Transactions.FindAsync(txId);
        tx!.IsCleared.Should().BeFalse();
    }

    [Fact]
    public async Task DisputeAsync_InsertsNewTransactionWithNeedsReview()
    {
        var txId = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        await _transactionService.MarkClearedAsync(txId, cleared: true);
        var staged = await CreateStagedAsync(txId);

        var countBefore = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);

        await _service.DisputeAsync(staged.Id);

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
    public async Task DisputeAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DisputeAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
