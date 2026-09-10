using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
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
[Collection("IntegrationParallel2")]
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

        var accountService          = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        _transactionService         = new TransactionService(_fixture.Db, accountService, liabilityPaymentService, attachmentService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

        _service = new ImportStagedTransactionService(_fixture.Db, _transactionService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

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

    /// <summary>
    /// Stage 7.5 / ADR-0068 property 1: the runtime app role (ceres_app, NOBYPASSRLS)
    /// CANNOT insert a staged-transaction row stamped to a foreign UserId. The RLS
    /// WITH CHECK policy rejects the write with SqlState 42501.
    /// </summary>
    [Fact]
    public async Task RuntimeApp_role_cannot_insert_staged_transaction_with_foreign_UserId()
    {
        var intruderUserId = Guid.NewGuid();
        _fixture.Db.ImportStagedTransactions.Add(new ImportStagedTransaction
        {
            Id              = Guid.NewGuid(),
            UserId          = intruderUserId,
            ImportedAt      = DateTime.UtcNow,
            AccountId       = _accountId,
            RawDate         = DateOnly.FromDateTime(DateTime.Today),
            RawAmount       = 100m,
            RawDescription  = "Foreign-UserId insert attempt",
        });

        var act = async () => await _fixture.Db.SaveChangesAsync();

        // Stage 7.6.2: RlsExceptionTranslator wraps 42501 on a user-owned table into
        // RlsPolicyViolationException with the diagnostic payload.
        var ex = await act.Should().ThrowAsync<ProjectCeres.Common.Exceptions.RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("ImportStagedTransactions");
        ex.Which.OriginalException.SqlState.Should().Be("42501");
    }

    /// <summary>
    /// Stage 7.5 / ADR-0068 property 2: even if a foreign-UserId row has somehow
    /// landed in the table (via the admin role, migrator, or a future regression),
    /// <see cref="ImportStagedTransactionService.TryConfirmAllAsync"/> run under the
    /// sentinel user does NOT confirm it. The service-layer filter pre-dates RLS
    /// (Stage 7) and stays as defence-in-depth (ADR-0065).
    ///
    /// The intruder account + staged row are seeded via the admin context (BYPASSRLS)
    /// so the FK target is visible across connections.
    /// </summary>
    [Fact]
    public async Task TryConfirmAllAsync_skips_foreign_UserId_rows_seeded_via_admin_path()
    {
        var ownTxId    = await CreateTransactionAsync(DateOnly.FromDateTime(DateTime.Today), 100m);
        var ownStaged  = await CreateStagedAsync(ownTxId, StagedTransactionStatus.Pending);

        var intruderUserId    = Guid.NewGuid();
        var intruderAccountId = Guid.NewGuid();
        var intruderStagedId  = Guid.NewGuid();
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.Add(new Account
            {
                Id            = intruderAccountId,
                UserId        = intruderUserId,
                Name          = $"Intruder Account {Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
            });
            admin.ImportStagedTransactions.Add(new ImportStagedTransaction
            {
                Id              = intruderStagedId,
                UserId          = intruderUserId,
                ImportedAt      = DateTime.UtcNow,
                AccountId       = intruderAccountId,
                RawDate         = DateOnly.FromDateTime(DateTime.Today),
                RawAmount       = 100m,
                RawDescription  = "Intruder CSV row (admin-seeded)",
                Status          = StagedTransactionStatus.Pending,
            });
            await admin.SaveChangesAsync();
        }

        try
        {
            var result = await _service.TryConfirmAllAsync();
            result.IsSuccess.Should().BeTrue();

            var ownReloaded = await _fixture.Db.ImportStagedTransactions.FindAsync(ownStaged.Id);
            ownReloaded!.Status.Should().Be(StagedTransactionStatus.Confirmed);
            ownReloaded.ResolvedAt.Should().NotBeNull();

            await using var verify = _fixture.CreateAdminContext();
            var intruder = await verify.ImportStagedTransactions
                .IgnoreQueryFilters()
                .SingleAsync(s => s.Id == intruderStagedId);
            intruder.Status.Should().Be(StagedTransactionStatus.Pending);
            intruder.ResolvedAt.Should().BeNull();
        }
        finally
        {
            // Admin-seeded rows live outside the fixture's per-test transaction; clean them up.
            await using var cleanup = _fixture.CreateAdminContext();
            await cleanup.ImportStagedTransactions
                .IgnoreQueryFilters()
                .Where(s => s.Id == intruderStagedId)
                .ExecuteDeleteAsync();
            await cleanup.Accounts
                .IgnoreQueryFilters()
                .Where(a => a.Id == intruderAccountId)
                .ExecuteDeleteAsync();
        }
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
