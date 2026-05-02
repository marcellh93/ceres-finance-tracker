using FluentAssertions;
using ProjectCeres.Common;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;
using Moq;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class TransferReviewServiceTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private TransferReviewService _service = null!;
    private Guid _accountA;
    private Guid _accountB;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        var accountService          = new AccountService(_fixture.Db, new SingleUserAccessor());
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService, new SingleUserAccessor());
        var attachmentMock          = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentMock, new SingleUserAccessor());
        var transferService         = new TransferService(_fixture.Db, accountService, new SingleUserAccessor());

        _service = new TransferReviewService(_fixture.Db, transferService, transactionService);

        var a = new Account { Id = Guid.NewGuid(), Name = "A", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var b = new Account { Id = Guid.NewGuid(), Name = "B", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        _fixture.Db.Accounts.AddRange(a, b);
        await _fixture.Db.SaveChangesAsync();
        _accountA = a.Id;
        _accountB = b.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<ImportStagedTransfer> SeedStagedRow(
        decimal rawAmount = -100m, Guid? candidateTransactionId = null)
    {
        var staged = new ImportStagedTransfer
        {
            Id                     = Guid.NewGuid(),
            ImportedAt             = DateTime.UtcNow,
            AccountId              = _accountA,
            RawDate                = new DateOnly(2024, 3, 1),
            RawAmount              = rawAmount,
            RawDescription         = "Transfer",
            CandidateTransactionId = candidateTransactionId,
            Status                 = StagedTransferStatus.Pending
        };
        _fixture.Db.ImportStagedTransfers.Add(staged);
        await _fixture.Db.SaveChangesAsync();
        return staged;
    }

    private async Task<Transaction> SeedTransaction(Guid accountId, decimal amount = 100m)
    {
        var txn = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(2024, 3, 1),
            Amount     = amount,
            AccountId  = accountId,
            CategoryId = new Guid("20000000-0000-0000-0000-000000000007"),
            CreatedAt  = DateTime.UtcNow
        };
        _fixture.Db.Transactions.Add(txn);
        await _fixture.Db.SaveChangesAsync();
        return txn;
    }

    [Fact]
    public async Task LinkToExisting_CreatesTransfer_MarkesStagedLinked()
    {
        var candidateTxn = await SeedTransaction(_accountB);
        var staged       = await SeedStagedRow(-100m, candidateTxn.Id);

        await _service.LinkToExistingAsync(staged.Id, _accountB);

        var updatedStaged = await _fixture.Db.ImportStagedTransfers.FindAsync(staged.Id);
        updatedStaged!.Status.Should().Be(StagedTransferStatus.Linked);
        updatedStaged.ResolvedAt.Should().NotBeNull();

        var transfers = await _fixture.Db.Transfers.ToListAsync();
        transfers.Should().HaveCount(1);
        transfers[0].Amount.Should().Be(100m);
    }

    [Fact]
    public async Task CreateAsTransfer_CreatesTransfer_MarkedCreatedAsTransfer()
    {
        var staged = await SeedStagedRow(-150m);

        await _service.CreateAsTransferAsync(staged.Id, otherAccountId: _accountB);

        var updatedStaged = await _fixture.Db.ImportStagedTransfers.FindAsync(staged.Id);
        updatedStaged!.Status.Should().Be(StagedTransferStatus.CreatedAsTransfer);
        updatedStaged.ResolvedAt.Should().NotBeNull();

        var transfers = await _fixture.Db.Transfers.ToListAsync();
        transfers.Should().HaveCount(1);
        transfers[0].SourceAccountId.Should().Be(_accountA);
        transfers[0].DestAccountId.Should().Be(_accountB);
        transfers[0].Amount.Should().Be(150m);
    }

    [Fact]
    public async Task DismissAsTransaction_CreatesTransaction_SavesExclusionPattern()
    {
        var staged = await SeedStagedRow(-75m);
        staged.RawDescription = "bizum payment abc";
        await _fixture.Db.SaveChangesAsync();

        await _service.DismissAsTransactionAsync(staged.Id);

        var updatedStaged = await _fixture.Db.ImportStagedTransfers.FindAsync(staged.Id);
        updatedStaged!.Status.Should().Be(StagedTransferStatus.DismissedAsTransaction);

        var txns = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountA)
            .ToListAsync();
        txns.Should().HaveCount(1);
        txns[0].Amount.Should().Be(75m);

        var exclusions = await _fixture.Db.ImportTransferExclusions.ToListAsync();
        exclusions.Should().HaveCount(1);
        exclusions[0].DescriptionPattern.Should().Be("bizum payment abc");
    }

    [Fact]
    public async Task DismissAsTransaction_DuplicateDescription_DoesNotDuplicateExclusion()
    {
        _fixture.Db.ImportTransferExclusions.Add(new ImportTransferExclusion
        {
            Id = Guid.NewGuid(), DescriptionPattern = "bizum", CreatedAt = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var staged = await SeedStagedRow(-40m);
        staged.RawDescription = "bizum";
        await _fixture.Db.SaveChangesAsync();

        await _service.DismissAsTransactionAsync(staged.Id);

        var count = await _fixture.Db.ImportTransferExclusions.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPendingRows()
    {
        var p = await SeedStagedRow(-10m);
        var resolved = await SeedStagedRow(-20m);
        resolved.Status = StagedTransferStatus.Linked;
        await _fixture.Db.SaveChangesAsync();

        var pending = await _service.GetPendingAsync();

        pending.Should().HaveCount(1);
        pending.First().Id.Should().Be(p.Id);
    }
}
