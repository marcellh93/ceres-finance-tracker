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

/// <summary>
/// Integration tests for ImportService.ImportAsync against the real project_ceres_test database.
///
/// Seeded system categories (stable GUIDs):
///   20000000-0000-0000-0000-000000000025 = Uncategorized Income  (CategoryTypeId = 1)
///   20000000-0000-0000-0000-000000000026 = Uncategorized Expense (CategoryTypeId = 2)
/// </summary>
[Collection("IntegrationTests")]
public class ImportServiceIntegrationTests : IAsyncLifetime
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly TestDbFixture _fixture = new();
    private ImportService _service = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        var accountService          = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(_fixture.Db, accountService, liabilityPaymentService, attachmentService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

        var stagedTransactionService = new ImportStagedTransactionService(_fixture.Db, transactionService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        var parserFactory = new ImportParserFactory(new CsvImportParser(), new ExcelImportParser());
        _service = new ImportService(parserFactory, TimeProvider.System, _fixture.Db, transactionService, stagedTransactionService: stagedTransactionService);

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

    [Fact]
    public async Task ImportAsync_ValidCsv_Inserts10TransactionsAllCleared()
    {
        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, StandardMappings());

        result.RowsImported.Should().Be(10);
        result.RowsReconciled.Should().Be(0);
        result.RowsFlagged.Should().Be(0);
        result.RowsFailed.Should().Be(0);

        var dbCount = await _fixture.Db.Transactions.CountAsync(t => t.AccountId == _accountId);
        dbCount.Should().Be(10);

        var allCleared = await _fixture.Db.Transactions
            .Where(t => t.AccountId == _accountId)
            .AllAsync(t => t.IsCleared);
        allCleared.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_ValidXlsx_Inserts10TransactionsAllCleared()
    {
        var file   = FileFromFixture("valid_import.xlsx");
        var result = await _service.ImportAsync(file, _accountId, StandardMappings());

        result.RowsImported.Should().Be(10);
        result.RowsReconciled.Should().Be(0);
        result.RowsFailed.Should().Be(0);
    }

    [Fact]
    public async Task ImportAsync_ValidCsv_TotalCountMatchesRowCount()
    {
        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, StandardMappings());

        (result.RowsImported + result.RowsReconciled + result.RowsFlagged + result.RowsFailed).Should().Be(10);
    }

    [Fact]
    public async Task ImportAsync_MatchingExistingTransaction_ReconcilesClearsItAndDoesNotDuplicate()
    {
        // Seed one pre-existing transaction matching first row of valid_import.csv
        // (assumes first row: 2024-01-01, 50.00, "Grocery store")
        var existingId = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id          = existingId,
            Date        = new DateOnly(2024, 1, 1),
            Amount      = 50.00m,
            Description = "Grocery store",
            AccountId   = _accountId,
            CategoryId  = UncategorizedExpenseId,
            IsCleared   = false,
            CreatedAt   = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, StandardMappings());

        // 1 reconciled, 9 imported, 0 duplicates created
        result.RowsReconciled.Should().Be(1);
        result.RowsImported.Should().Be(9);

        // Total transactions in DB = 10 (1 pre-existing + 9 new), not 11
        var count = await _fixture.Db.Transactions.CountAsync(t => t.AccountId == _accountId);
        count.Should().Be(10);

        // The pre-existing one must now be cleared
        var existing = await _fixture.Db.Transactions.FindAsync(existingId);
        existing!.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_PositiveAmount_AssignsUncategorizedIncome()
    {
        // Create a CSV with one positive-amount row
        var csv = "Date,Amount,Description\n2024-03-01,200.00,Salary\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns("income.csv");
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });

        var result = await _service.ImportAsync(mock.Object, _accountId, new ImportColumnMappings
        {
            DateColumn = "Date", AmountColumn = "Amount", DescriptionColumn = "Description"
        });

        result.RowsImported.Should().Be(1);

        var txn = await _fixture.Db.Transactions.FirstAsync(t => t.AccountId == _accountId);
        txn.CategoryId.Should().Be(UncategorizedIncomeId);
        txn.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_ReconciledRow_CreatesStagedTransactionRecord()
    {
        // Seed one pre-existing transaction matching first row of valid_import.csv
        // (2024-01-01, 50.00, "Grocery store")
        var existingId = Guid.NewGuid();
        _fixture.Db.Transactions.Add(new Transaction
        {
            Id          = existingId,
            Date        = new DateOnly(2024, 1, 1),
            Amount      = 50.00m,
            Description = "Grocery store",
            AccountId   = _accountId,
            CategoryId  = UncategorizedExpenseId,
            IsCleared   = false,
            CreatedAt   = DateTime.UtcNow
        });
        await _fixture.Db.SaveChangesAsync();

        var file   = FileFromFixture("valid_import.csv");
        var result = await _service.ImportAsync(file, _accountId, StandardMappings());

        result.RowsReconciled.Should().Be(1);

        var staged = await _fixture.Db.ImportStagedTransactions
            .Where(s => s.MatchedTransactionId == existingId)
            .ToListAsync();

        staged.Should().ContainSingle();
        staged[0].Status.Should().Be(StagedTransactionStatus.Pending);
        staged[0].RawAmount.Should().Be(50.00m);
        staged[0].AccountId.Should().Be(_accountId);
    }

    [Fact]
    public async Task ImportAsync_NegativeAmount_AssignsUncategorizedExpense()
    {
        var csv = "Date,Amount,Description\n2024-03-01,-50.00,Coffee\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns("expense.csv");
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });

        var result = await _service.ImportAsync(mock.Object, _accountId, new ImportColumnMappings
        {
            DateColumn = "Date", AmountColumn = "Amount", DescriptionColumn = "Description"
        });

        result.RowsImported.Should().Be(1);

        var txn = await _fixture.Db.Transactions.FirstAsync(t => t.AccountId == _accountId);
        txn.CategoryId.Should().Be(UncategorizedExpenseId);
        txn.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_NegativeAmountWithFlipDebitSignTrue_StaysExpense()
    {
        // Required by spec 2026-04-29: this is the case that shipped broken — toggle ON
        // (negative-debits convention, the user's actual setup) was inverting the sign
        // and classifying expense rows as income. The pipeline must keep -75 negative.
        var csv = "Date,Amount,Description\n2024-03-01,-75.00,Coffee\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns("expense.csv");
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });

        var result = await _service.ImportAsync(mock.Object, _accountId, new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description",
            FlipDebitSign     = true
        });

        result.RowsImported.Should().Be(1);

        var txn = await _fixture.Db.Transactions.FirstAsync(t => t.AccountId == _accountId);
        txn.CategoryId.Should().Be(UncategorizedExpenseId);
        txn.Amount.Should().Be(75.00m); // ImportService stores Math.Abs(rawAmount)
    }
}
