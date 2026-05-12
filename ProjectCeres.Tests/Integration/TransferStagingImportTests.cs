using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class TransferStagingImportTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private ImportService _service = null!;
    private Guid _accountA;
    private Guid _accountB;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        var accountService          = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(_fixture.Db, accountService, liabilityPaymentService, attachmentService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")));
        var detectionService        = new TransferDetectionService();

        var parserFactory = new ImportParserFactory(new CsvImportParser(), new ExcelImportParser());
        _service = new ImportService(parserFactory, _fixture.Db, transactionService, detectionService);

        var a = new Account { Id = Guid.NewGuid(), Name = "Account A", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var b = new Account { Id = Guid.NewGuid(), Name = "Account B", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        _fixture.Db.Accounts.AddRange(a, b);
        await _fixture.Db.SaveChangesAsync();
        _accountA = a.Id;
        _accountB = b.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static IFormFile CsvFile(string content, string name = "test.csv")
    {
        var bytes  = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var mock   = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(name);
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });
        return mock.Object;
    }

    private static ImportColumnMappings Mappings() => new()
    {
        DateColumn = "Date", AmountColumn = "Amount", DescriptionColumn = "Description"
    };

    [Fact]
    public async Task ImportAsync_IntraFilePair_BothStagedNeitherInsertedAsTransaction()
    {
        var csv = "Date,Amount,Description\n2024-03-01,100.00,Transfer out\n2024-03-01,-100.00,Transfer in\n";
        var file = CsvFile(csv);

        var result = await _service.ImportAsync(file, _accountA, Mappings());

        result.RowsStaged.Should().Be(2);
        result.RowsImported.Should().Be(0);

        var txCount = await _fixture.Db.Transactions.CountAsync(t => t.AccountId == _accountA);
        txCount.Should().Be(0);

        var staged = await _fixture.Db.ImportStagedTransfers
            .Where(s => s.AccountId == _accountA)
            .ToListAsync();
        staged.Should().HaveCount(2);
        staged.Should().AllSatisfy(s => s.Status.Should().Be(ProjectCeres.Models.StagedTransferStatus.Pending));
    }

    [Fact]
    public async Task ImportAsync_CrossAccountMatch_StagedWithCandidateId()
    {
        var candidateId = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id          = candidateId,
            Date        = new DateOnly(2024, 3, 1),
            Amount      = 250m,
            AccountId   = _accountB,
            CategoryId  = new Guid("20000000-0000-0000-0000-000000000007"),
            CreatedAt   = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var csv  = "Date,Amount,Description\n2024-03-01,-250.00,Wire to Account B\n";
        var file = CsvFile(csv);

        var result = await _service.ImportAsync(file, _accountA, Mappings());

        result.RowsStaged.Should().Be(1);
        result.RowsImported.Should().Be(0);

        var staged = await _fixture.Db.ImportStagedTransfers
            .FirstAsync(s => s.AccountId == _accountA);
        staged.CandidateTransactionId.Should().Be(candidateId);
    }

    [Fact]
    public async Task ImportAsync_RowMatchesExclusion_NotStaged_ImportedAsTransaction()
    {
        _fixture.Db.ImportTransferExclusions.Add(new ImportTransferExclusion
        {
            Id                 = Guid.NewGuid(),
            DescriptionPattern = "bizum",
            CreatedAt          = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var csv = "Date,Amount,Description\n2024-03-01,100.00,BIZUM payment\n2024-03-01,-100.00,BIZUM receive\n";
        var file = CsvFile(csv);

        var result = await _service.ImportAsync(file, _accountA, Mappings());

        result.RowsStaged.Should().Be(0);
        result.RowsImported.Should().Be(2);
    }
}
