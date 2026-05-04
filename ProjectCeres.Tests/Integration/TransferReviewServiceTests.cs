using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
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

        _service = new TransferReviewService(_fixture.Db, transferService, transactionService, new SingleUserAccessor());

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
