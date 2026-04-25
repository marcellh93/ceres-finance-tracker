using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for ImportService.ImportAsync against the real project_ceres_test database.
/// Each test rolls back — no data persists.
///
/// Seed data:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense)
/// </summary>
[Collection("IntegrationTests")]
public class ImportServiceIntegrationTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly TestDbFixture _fixture = new();
    private ImportService _service = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        var accountService         = new AccountService(_fixture.Db);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService);
        var attachmentService      = new Mock<IFileAttachmentService>().Object;
        var transactionService     = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentService);

        _service = new ImportService(_fixture.Db, transactionService);

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Import Test Account {Guid.NewGuid():N}",
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

    private static IFormFile FileFromFixture(string fileName)
    {
        var path   = Path.Combine(FixturesDir, fileName);
        var bytes  = File.ReadAllBytes(path);
        var stream = new MemoryStream(bytes);
        var file   = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });
        return file.Object;
    }

    private static ImportColumnMappings StandardMappings() => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        CategoryColumn    = "Category"
    };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImportAsync_ValidCsv_Inserts10TransactionsAllCleared()
    {
        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, HousingCategoryId, StandardMappings());

        result.RowsImported.Should().Be(10);
        result.RowsFlagged.Should().Be(0);
        result.RowsFailed.Should().Be(0);

        var dbCount = await _fixture.Db.Transactions
            .CountAsync(t => t.AccountId == _accountId);
        dbCount.Should().Be(10);

        var allCleared = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId)
            .AllAsync(t => t.IsCleared);
        allCleared.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_DuplicateCandidates_FlaggedAsIsClearedFalse()
    {
        // Seed one existing transaction matching the first row of duplicate_candidates.csv
        // (2024-01-01, 50.00, "Grocery store") exactly — should be flagged.
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = new DateOnly(2024, 1, 1),
            Amount      = 50.00m,
            Description = "Grocery store",
            AccountId   = _accountId,
            CategoryId  = HousingCategoryId,
            IsCleared   = true,
            CreatedAt   = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var file   = FileFromFixture("duplicate_candidates.csv");
        var result = await _service.ImportAsync(file, _accountId, HousingCategoryId, StandardMappings());

        // 3 rows total: 1 flagged (exact match), 2 clean
        result.RowsFlagged.Should().Be(1);
        result.RowsImported.Should().Be(2);
        result.RowsFailed.Should().Be(0);

        // Flagged row must have IsCleared = false.
        var flagged = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId && !t.IsCleared && t.Amount == 50.00m)
            .ToListAsync();
        flagged.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImportAsync_ValidCsv_ResultCountsAreCorrect()
    {
        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, HousingCategoryId, StandardMappings());

        var totalProcessed = result.RowsImported + result.RowsFlagged + result.RowsFailed;
        totalProcessed.Should().Be(10);
    }
}
