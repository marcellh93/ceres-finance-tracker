# Import UX Redesign — Plan A: Core Import Engine

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the import form with progressive disclosure, auto-detected column headers, sign-based category fallback (Uncategorized Income/Expense), and reconciliation matching (match by amount+date → clear existing transaction instead of creating duplicate).

**Architecture:** The form starts with three fields; uploading a file triggers a JS API call (`GET /import/headers`) that returns detected headers, which populate column-mapping dropdowns. `ImportService.ImportAsync` gains a reconciliation pass before creating any transaction. Two new system categories seed via EF migration. The Summary page gains a Reconciled count card and a save-profile prompt.

**Tech Stack:** ASP.NET Core MVC, Razor, EF Core / PostgreSQL, Tailwind CSS v3, vanilla JS (no framework), xUnit + FluentAssertions

---

## File Map

**New files:**
- `ProjectCeres/Controllers/Api/ImportHeadersController.cs` — API endpoint: parse uploaded file, return detected headers + auto-match suggestions
- `ProjectCeres/Services/IHeaderDetectionService.cs` — interface for header detection + auto-match logic
- `ProjectCeres/Services/HeaderDetectionService.cs` — implementation of auto-match rules
- `ProjectCeres/ViewModels/HeaderDetectionResult.cs` — response shape for the headers API
- `ProjectCeres.Tests/Unit/HeaderDetectionServiceTests.cs` — unit tests for auto-match logic

**Modified files:**
- `ProjectCeres/Data/AppDbContext.cs` — add two new system categories to `SeedCategories`
- `ProjectCeres/Services/IImportService.cs` — update `ImportAsync` signature (remove `categoryId` param)
- `ProjectCeres/Services/ImportService.cs` — add reconciliation pass; infer category from amount sign; remove `categoryId` param
- `ProjectCeres/ViewModels/ImportResult.cs` — add `RowsReconciled` property
- `ProjectCeres/ViewModels/ImportSummaryViewModel.cs` — add `RowsReconciled`, `PendingProfileSave` properties; update `TotalProcessed`
- `ProjectCeres/ViewModels/ImportUploadViewModel.cs` — remove `CategoryId`; column fields become optional (populated via JS)
- `ProjectCeres/Controllers/ImportController.cs` — remove `CategoryId` from form handling; pass updated `ImportResult` to Summary; add save-profile POST action
- `ProjectCeres/Views/Import/Index.cshtml` — progressive disclosure form; column dropdowns; remove default category
- `ProjectCeres/Views/Import/Summary.cshtml` — add Reconciled card; add save-profile prompt
- `ProjectCeres.Tests/Integration/ImportServiceTests.cs` — update existing tests for new signature; add reconciliation tests
- `ProjectCeres.Tests/Integration/ImportApiTests.cs` — add test for `/import/headers` endpoint

**EF Migration (generated, not hand-written):**
- `ProjectCeres/Migrations/AddUncategorizedSystemCategories.cs` — seeds two new Category rows

---

## Task 1: Seed Uncategorized Income and Uncategorized Expense system categories

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`

The two new categories need stable GUIDs so EF migrations can track them.

- [ ] **Step 1: Add seed data**

In `AppDbContext.cs`, find the `SeedCategories` method and add these two rows at the top of the `HasData` call (after "Opening Balance"):

```csharp
new Category { Id = new Guid("20000000-0000-0000-0000-000000000025"), Name = "Uncategorized Income",  CategoryTypeId = 1, IsActive = true, IsSystem = true,  LifestyleTag = null },
new Category { Id = new Guid("20000000-0000-0000-0000-000000000026"), Name = "Uncategorized Expense", CategoryTypeId = 2, IsActive = true, IsSystem = true,  LifestyleTag = null },
```

- [ ] **Step 2: Create and apply migration**

```bash
dotnet ef migrations add AddUncategorizedSystemCategories --project ProjectCeres
dotnet ef database update --project ProjectCeres
```

Expected: migration created, database updated with no errors.

- [ ] **Step 3: Verify in tests**

```bash
dotnet test --filter "FullyQualifiedName~CategoryServiceTests" --logger "console;verbosity=normal"
```

Expected: all category tests pass (existing tests should be unaffected).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs ProjectCeres/Migrations/
git commit -m "feat: seed Uncategorized Income and Uncategorized Expense system categories"
```

---

## Task 2: HeaderDetectionService — auto-match column names from file headers

**Files:**
- Create: `ProjectCeres/Services/IHeaderDetectionService.cs`
- Create: `ProjectCeres/Services/HeaderDetectionService.cs`
- Create: `ProjectCeres/ViewModels/HeaderDetectionResult.cs`
- Create: `ProjectCeres.Tests/Unit/HeaderDetectionServiceTests.cs`

- [ ] **Step 1: Create the result ViewModel**

```csharp
// ProjectCeres/ViewModels/HeaderDetectionResult.cs
namespace ProjectCeres.ViewModels;

public class HeaderDetectionResult
{
    public IReadOnlyList<string> Headers { get; init; } = [];
    public string? DateColumn { get; init; }
    public string? AmountColumn { get; init; }
    public string? DescriptionColumn { get; init; }
    public string? CategoryColumn { get; init; }
}
```

- [ ] **Step 2: Create the interface**

```csharp
// ProjectCeres/Services/IHeaderDetectionService.cs
using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IHeaderDetectionService
{
    Task<HeaderDetectionResult> DetectAsync(IFormFile file);
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// ProjectCeres.Tests/Unit/HeaderDetectionServiceTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Services;
using System.Text;

namespace ProjectCeres.Tests.Unit;

public class HeaderDetectionServiceTests
{
    private static IFormFile CsvFile(string headers)
    {
        var content = headers + "\n2024-01-01,100.00,Test\n";
        var bytes   = Encoding.UTF8.GetBytes(content);
        var stream  = new MemoryStream(bytes);
        var mock    = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns("test.csv");
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });
        return mock.Object;
    }

    [Fact]
    public async Task DetectAsync_SpanishBankHeaders_MatchesCorrectly()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("F.Valor,Fecha,Concepto,Movimiento,Importe,Divisa");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().Contain("Fecha");
        result.Headers.Should().Contain("Importe");
        result.DateColumn.Should().Be("Fecha");
        result.AmountColumn.Should().Be("Importe");
        result.DescriptionColumn.Should().Be("Concepto");
        result.CategoryColumn.Should().BeNull(); // no category keyword match
    }

    [Fact]
    public async Task DetectAsync_EnglishHeaders_MatchesCorrectly()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Date,Amount,Description,Category");
        var result = await svc.DetectAsync(file);

        result.DateColumn.Should().Be("Date");
        result.AmountColumn.Should().Be("Amount");
        result.DescriptionColumn.Should().Be("Description");
        result.CategoryColumn.Should().Be("Category");
    }

    [Fact]
    public async Task DetectAsync_NoMatch_ReturnsNullSuggestions()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Col1,Col2,Col3");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().BeEquivalentTo(["Col1", "Col2", "Col3"]);
        result.DateColumn.Should().BeNull();
        result.AmountColumn.Should().BeNull();
        result.DescriptionColumn.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_ReturnsAllHeaders()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Fecha,Importe,Concepto,Observaciones");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().HaveCount(4);
        result.Headers.Should().Contain("Observaciones");
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~HeaderDetectionServiceTests" --logger "console;verbosity=normal"
```

Expected: compilation error — `HeaderDetectionService` not found.

- [ ] **Step 5: Implement HeaderDetectionService**

```csharp
// ProjectCeres/Services/HeaderDetectionService.cs
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;
using System.Globalization;

namespace ProjectCeres.Services;

public class HeaderDetectionService : IHeaderDetectionService
{
    private static readonly string[] DateKeywords        = ["fecha", "date", "data", "datum", "f.valor"];
    private static readonly string[] AmountKeywords      = ["importe", "amount", "monto", "betrag"];
    private static readonly string[] DescriptionKeywords = ["concepto", "description", "descripcion", "memo", "details"];
    private static readonly string[] CategoryKeywords    = ["categoria", "category", "tipo"];

    public async Task<HeaderDetectionResult> DetectAsync(IFormFile file)
    {
        var headers = await ReadHeadersAsync(file);

        return new HeaderDetectionResult
        {
            Headers           = headers,
            DateColumn        = BestMatch(headers, DateKeywords),
            AmountColumn      = BestMatch(headers, AmountKeywords),
            DescriptionColumn = BestMatch(headers, DescriptionKeywords),
            CategoryColumn    = BestMatch(headers, CategoryKeywords)
        };
    }

    private static string? BestMatch(IReadOnlyList<string> headers, string[] keywords)
    {
        foreach (var header in headers)
        {
            var lower = header.ToLowerInvariant();
            if (keywords.Any(k => lower.Contains(k)))
                return header;
        }
        return null;
    }

    private static async Task<IReadOnlyList<string>> ReadHeadersAsync(IFormFile file)
    {
        using var mem = new MemoryStream();
        await file.CopyToAsync(mem);
        mem.Position = 0;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext == ".xlsx")
            return ReadXlsxHeaders(mem);

        using var reader = new StreamReader(mem);
        using var csv    = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated   = null
        });
        await csv.ReadAsync();
        csv.ReadHeader();
        return csv.HeaderRecord?.ToList() ?? [];
    }

    private static IReadOnlyList<string> ReadXlsxHeaders(Stream stream)
    {
        // Scan rows until we find one where all non-empty cells look like text (not dates/numbers).
        // For simplicity in Phase 2: read the first non-empty row.
        using var wb = new ClosedXML.Excel.XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        foreach (var row in ws.RowsUsed())
        {
            var cells = row.CellsUsed().Select(c => c.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (cells.Count >= 2)
                return cells;
        }
        return [];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~HeaderDetectionServiceTests" --logger "console;verbosity=normal"
```

Expected: 4 tests pass.

- [ ] **Step 7: Register service in DI**

In `ProjectCeres/Program.cs`, find the existing service registrations and add:

```csharp
builder.Services.AddScoped<IHeaderDetectionService, HeaderDetectionService>();
```

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Services/IHeaderDetectionService.cs \
        ProjectCeres/Services/HeaderDetectionService.cs \
        ProjectCeres/ViewModels/HeaderDetectionResult.cs \
        ProjectCeres.Tests/Unit/HeaderDetectionServiceTests.cs \
        ProjectCeres/Program.cs
git commit -m "feat: add HeaderDetectionService — auto-match CSV/XLSX headers to Ceres fields"
```

---

## Task 3: Import Headers API endpoint

**Files:**
- Create: `ProjectCeres/Controllers/Api/ImportHeadersController.cs`
- Modify: `ProjectCeres.Tests/Integration/ImportApiTests.cs`

- [ ] **Step 1: Write the failing test**

Open `ProjectCeres.Tests/Integration/ImportApiTests.cs` and add this test to the existing class:

```csharp
[Fact]
public async Task GetHeaders_ValidCsv_ReturnsDetectedHeaders()
{
    var csv = "Fecha,Importe,Concepto\n17/04/2026,100.00,Test\n";
    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "file", "test.csv");

    var response = await _client.PostAsync("/api/import/headers", content);

    response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<ProjectCeres.ViewModels.HeaderDetectionResult>();
    body.Should().NotBeNull();
    body!.Headers.Should().Contain("Fecha");
    body.DateColumn.Should().Be("Fecha");
    body.AmountColumn.Should().Be("Importe");
    body.DescriptionColumn.Should().Be("Concepto");
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test ProjectCeres.Tests --filter "GetHeaders_ValidCsv_ReturnsDetectedHeaders" --logger "console;verbosity=normal"
```

Expected: FAIL — 404 Not Found.

- [ ] **Step 3: Create the controller**

```csharp
// ProjectCeres/Controllers/Api/ImportHeadersController.cs
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/import/headers")]
public class ImportHeadersController(IHeaderDetectionService headerDetectionService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Detect(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        try
        {
            var result = await headerDetectionService.DetectAsync(file);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test ProjectCeres.Tests --filter "GetHeaders_ValidCsv_ReturnsDetectedHeaders" --logger "console;verbosity=normal"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/ImportHeadersController.cs \
        ProjectCeres.Tests/Integration/ImportApiTests.cs
git commit -m "feat: add /api/import/headers endpoint for client-side column detection"
```

---

## Task 4: Update ImportService — reconciliation pass + sign-based category fallback

**Files:**
- Modify: `ProjectCeres/Services/IImportService.cs`
- Modify: `ProjectCeres/Services/ImportService.cs`
- Modify: `ProjectCeres/ViewModels/ImportResult.cs`
- Modify: `ProjectCeres.Tests/Integration/ImportServiceTests.cs`

The `categoryId` parameter is removed from `ImportAsync`. The service now looks up `Uncategorized Income` / `Uncategorized Expense` GUIDs at startup (they are seeded constants). Reconciliation: if a matching transaction exists (same account, same amount, date ±1 day), set `IsCleared = true` on it and skip creation.

- [ ] **Step 1: Update ImportResult**

```csharp
// ProjectCeres/ViewModels/ImportResult.cs
namespace ProjectCeres.ViewModels;

public class ImportResult
{
    public int RowsImported    { get; set; }
    public int RowsReconciled  { get; set; }
    public int RowsFlagged     { get; set; }
    public int RowsFailed      { get; set; }
    public List<string> Errors { get; set; } = [];
}
```

- [ ] **Step 2: Update the interface**

```csharp
// ProjectCeres/Services/IImportService.cs
using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportService
{
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings);
    string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId);
    Task<ImportResult> ImportAsync(IFormFile file, Guid accountId, ImportColumnMappings mappings);
}
```

- [ ] **Step 3: Write the failing tests**

Replace the contents of `ProjectCeres.Tests/Integration/ImportServiceTests.cs` with:

```csharp
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

        var accountService          = new AccountService(_fixture.Db);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService);
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentService);

        var parserFactory = new ImportParserFactory(new CsvImportParser(), new ExcelImportParser());
        _service = new ImportService(parserFactory, _fixture.Db, transactionService);

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
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ImportServiceIntegration" --logger "console;verbosity=normal"
```

Expected: compilation errors — `ImportAsync` signature mismatch, `RowsReconciled` missing.

- [ ] **Step 5: Implement the updated ImportService**

Replace `ProjectCeres/Services/ImportService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportService(
    ImportParserFactory parserFactory,
    AppDbContext? db = null,
    ITransactionService? transactionService = null) : IImportService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public async Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, ImportColumnMappings mappings)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var format    = extension switch
        {
            ".csv"  => ImportFormat.Csv,
            ".xlsx" => ImportFormat.Excel,
            _       => throw new InvalidOperationException(
                           $"Unsupported file format '{extension}'. Please upload a CSV or Excel (.xlsx) file.")
        };
        var parser = parserFactory.GetParser(format);
        return await parser.ParseAsync(file, mappings);
    }

    public string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId)
    {
        var raw  = $"{date:yyyy-MM-dd}|{amount:F2}|{description ?? ""}|{accountId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<ImportResult> ImportAsync(
        IFormFile file, Guid accountId, ImportColumnMappings mappings)
    {
        if (db is null || transactionService is null)
            throw new InvalidOperationException("ImportService requires db and transactionService for ImportAsync.");

        var rows   = await ParseAsync(file, mappings);
        var result = new ImportResult();

        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        foreach (var row in rows)
        {
            try
            {
                // Reconciliation pass: match by amount + date ±1 day
                var match = existingTxns.FirstOrDefault(e =>
                    e.Amount == row.Amount &&
                    Math.Abs((e.Date.ToDateTime(TimeOnly.MinValue) -
                              row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= 1);

                if (match is not null)
                {
                    if (!match.IsCleared)
                    {
                        await transactionService.MarkClearedAsync(match.Id, cleared: true);
                    }
                    result.RowsReconciled++;
                    continue;
                }

                // No match — create new transaction
                var categoryId = row.Amount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;

                var vm = new TransactionCreateViewModel
                {
                    Date        = row.Date,
                    Amount      = Math.Abs(row.Amount), // store positive; direction from category type
                    Description = row.Description,
                    AccountId   = accountId,
                    CategoryId  = categoryId
                };

                var txId = await transactionService.CreateAsync(vm);
                await transactionService.MarkClearedAsync(txId, cleared: true);
                await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);
                result.RowsImported++;
            }
            catch (Exception ex)
            {
                result.RowsFailed++;
                result.Errors.Add($"Row {row.Date} {row.Amount}: {ex.Message}");
            }
        }

        return result;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ImportServiceIntegration" --logger "console;verbosity=normal"
```

Expected: all tests pass.

- [ ] **Step 7: Run the full test suite to check for regressions**

```bash
dotnet test ProjectCeres.Tests --logger "console;verbosity=normal"
```

Expected: all tests pass. If any test fails due to the removed `categoryId` parameter, fix the call site.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Services/IImportService.cs \
        ProjectCeres/Services/ImportService.cs \
        ProjectCeres/ViewModels/ImportResult.cs \
        ProjectCeres.Tests/Integration/ImportServiceTests.cs
git commit -m "feat: import reconciliation — match existing transactions instead of duplicating; sign-based category fallback"
```

---

## Task 5: Update ImportController — remove categoryId, wire reconciliation result

**Files:**
- Modify: `ProjectCeres/Controllers/ImportController.cs`
- Modify: `ProjectCeres/ViewModels/ImportUploadViewModel.cs`
- Modify: `ProjectCeres/ViewModels/ImportSummaryViewModel.cs`

- [ ] **Step 1: Update ImportUploadViewModel**

```csharp
// ProjectCeres/ViewModels/ImportUploadViewModel.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectCeres.ViewModels;

public class ImportUploadViewModel
{
    [Required(ErrorMessage = "Please select a file.")]
    public IFormFile? File { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    public Guid? ProfileId { get; set; }

    // Manual column mapping — populated by JS after file upload
    public string? DateColumn { get; set; }
    public string? AmountColumn { get; set; }
    public string? DescriptionColumn { get; set; }
    public string? CategoryColumn { get; set; }
    public bool FlipDebitSign { get; set; }
}
```

- [ ] **Step 2: Update ImportSummaryViewModel**

```csharp
// ProjectCeres/ViewModels/ImportSummaryViewModel.cs
namespace ProjectCeres.ViewModels;

public class ImportSummaryViewModel
{
    public int RowsImported   { get; set; }
    public int RowsReconciled { get; set; }
    public int RowsFlagged    { get; set; }
    public int RowsFailed     { get; set; }
    public List<string> Errors { get; set; } = [];

    // Set when the import used manual column mappings (no saved profile) — offer to save
    public ImportColumnMappings? MappingsToSave { get; set; }

    public int TotalProcessed => RowsImported + RowsReconciled + RowsFlagged + RowsFailed;
}
```

- [ ] **Step 3: Update ImportController**

Replace `ProjectCeres/Controllers/ImportController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ImportController(
    IImportService importService,
    IImportProfileService profileService,
    AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        await PopulateViewBagAsync();
        return View(new ImportUploadViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ImportUploadViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateViewBagAsync();
            return View(vm);
        }

        const long MaxImportFileBytes = 10 * 1024 * 1024;
        if (vm.File!.Length > MaxImportFileBytes)
        {
            ModelState.AddModelError("File", "The import file exceeds the 10 MB size limit.");
            await PopulateViewBagAsync();
            return View(vm);
        }

        ImportColumnMappings mappings;
        bool usedProfile = false;

        if (vm.ProfileId.HasValue)
        {
            var profile = await profileService.GetByIdAsync(vm.ProfileId.Value);
            if (profile is null)
            {
                ModelState.AddModelError("ProfileId", "Selected profile not found.");
                await PopulateViewBagAsync();
                return View(vm);
            }
            mappings = profile.Mappings;
            usedProfile = true;
        }
        else
        {
            mappings = new ImportColumnMappings
            {
                DateColumn        = vm.DateColumn ?? "Date",
                AmountColumn      = vm.AmountColumn ?? "Amount",
                DescriptionColumn = vm.DescriptionColumn ?? "Description",
                CategoryColumn    = vm.CategoryColumn,
                FlipDebitSign     = vm.FlipDebitSign
            };
        }

        try
        {
            var result = await importService.ImportAsync(vm.File!, vm.AccountId!.Value, mappings);

            TempData["ImportRowsImported"]   = result.RowsImported;
            TempData["ImportRowsReconciled"] = result.RowsReconciled;
            TempData["ImportRowsFlagged"]    = result.RowsFlagged;
            TempData["ImportRowsFailed"]     = result.RowsFailed;
            TempData["ImportErrors"]         = string.Join("\n", result.Errors);

            // Offer to save mappings only when manual mapping was used
            if (!usedProfile)
            {
                TempData["SaveMappingsDate"]        = mappings.DateColumn;
                TempData["SaveMappingsAmount"]      = mappings.AmountColumn;
                TempData["SaveMappingsDescription"] = mappings.DescriptionColumn;
                TempData["SaveMappingsCategory"]    = mappings.CategoryColumn;
                TempData["SaveMappingsFlipDebit"]   = mappings.FlipDebitSign;
            }

            return RedirectToAction(nameof(Summary));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await PopulateViewBagAsync();
            return View(vm);
        }
    }

    public IActionResult Summary()
    {
        var vm = new ImportSummaryViewModel
        {
            RowsImported   = (int)(TempData["ImportRowsImported"]   ?? 0),
            RowsReconciled = (int)(TempData["ImportRowsReconciled"] ?? 0),
            RowsFlagged    = (int)(TempData["ImportRowsFlagged"]    ?? 0),
            RowsFailed     = (int)(TempData["ImportRowsFailed"]     ?? 0),
            Errors         = ((string?)TempData["ImportErrors"] ?? string.Empty)
                              .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .ToList()
        };

        // Reconstitute mappings-to-save if they were passed via TempData
        if (TempData["SaveMappingsDate"] is string dateCol)
        {
            vm.MappingsToSave = new ImportColumnMappings
            {
                DateColumn        = dateCol,
                AmountColumn      = (string?)TempData["SaveMappingsAmount"]      ?? "Amount",
                DescriptionColumn = (string?)TempData["SaveMappingsDescription"] ?? "Description",
                CategoryColumn    = (string?)TempData["SaveMappingsCategory"],
                FlipDebitSign     = (bool?)TempData["SaveMappingsFlipDebit"]     ?? false
            };
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(string profileName,
        string dateColumn, string amountColumn, string descriptionColumn,
        string? categoryColumn, bool flipDebitSign)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            TempData["ErrorMessage"] = "Profile name is required.";
            return RedirectToAction(nameof(Summary));
        }

        var mappings = new ImportColumnMappings
        {
            DateColumn        = dateColumn,
            AmountColumn      = amountColumn,
            DescriptionColumn = descriptionColumn,
            CategoryColumn    = categoryColumn,
            FlipDebitSign     = flipDebitSign
        };

        await profileService.CreateAsync(profileName, Models.ImportFormat.Csv, mappings);
        TempData["SuccessMessage"] = $"Profile \"{profileName}\" saved.";
        return RedirectToAction(nameof(Summary));
    }

    private async Task PopulateViewBagAsync()
    {
        ViewBag.Profiles = new SelectList(
            await profileService.GetAllActiveAsync(), "Id", "Name");

        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),
            "Id", "Name");
    }
}
```

- [ ] **Step 4: Run the full test suite**

```bash
dotnet test ProjectCeres.Tests --logger "console;verbosity=normal"
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/ImportController.cs \
        ProjectCeres/ViewModels/ImportUploadViewModel.cs \
        ProjectCeres/ViewModels/ImportSummaryViewModel.cs
git commit -m "feat: remove categoryId from import form; wire reconciliation counts and save-profile offer to summary"
```

---

## Task 6: Redesign the import form view — progressive disclosure

**Files:**
- Modify: `ProjectCeres/Views/Import/Index.cshtml`

The form starts with two fields. After the user picks a file, JS POSTs it to `/api/import/headers`, gets back detected headers, and populates the column-mapping dropdowns. If profiles exist (passed via `ViewBag.Profiles`), a profile dropdown appears at the top of the mapping section.

- [ ] **Step 1: Replace Index.cshtml**

```cshtml
@model ImportUploadViewModel
@{ ViewData["Title"] = "Import Transactions"; }

<div class="page-header">
    <h1>Import Transactions</h1>
</div>

@if (!string.IsNullOrEmpty(TempData["ErrorMessage"]?.ToString()))
{
    <div class="alert alert-danger">@TempData["ErrorMessage"]</div>
}

<form asp-action="Index" method="post" enctype="multipart/form-data" class="form-standard" id="import-form">
    @Html.AntiForgeryToken()
    <div asp-validation-summary="ModelOnly" class="alert alert-danger"></div>

    <div class="form-group">
        <label asp-for="File">Bank Statement File</label>
        <input asp-for="File" type="file" accept=".csv,.xlsx" class="form-control" id="import-file-input" />
        <span asp-validation-for="File" class="field-error"></span>
        <p class="form-hint">CSV and Excel (.xlsx) files are supported.</p>
    </div>

    <div class="form-group">
        <label asp-for="AccountId">Destination Account</label>
        <select asp-for="AccountId" asp-items="ViewBag.Accounts" class="form-control">
            <option value="">— Select Account —</option>
        </select>
        <span asp-validation-for="AccountId" class="field-error"></span>
    </div>

    {{!-- Column mapping section — hidden until a file is chosen --}}
    <div id="column-mapping-section" style="display:none">
        <hr class="my-4" />

        @if (ViewBag.Profiles is Microsoft.AspNetCore.Mvc.Rendering.SelectList profiles && profiles.Any())
        {
            <div class="form-group">
                <label asp-for="ProfileId">Use Saved Profile <span class="form-optional">(optional)</span></label>
                <select asp-for="ProfileId" asp-items="ViewBag.Profiles" class="form-control" id="profile-select">
                    <option value="">— Map columns manually —</option>
                </select>
            </div>
        }

        <div id="manual-mapping">
            <h3 class="form-section-title">Column Mapping</h3>
            <p class="form-hint mb-4">Match your file's column names to the fields below. We've pre-selected the closest matches.</p>

            <div class="form-group">
                <label asp-for="DateColumn">Date Column</label>
                <select asp-for="DateColumn" class="form-control column-dropdown" id="date-column-select">
                    <option value="">— Select column —</option>
                </select>
            </div>

            <div class="form-group">
                <label asp-for="AmountColumn">Amount Column</label>
                <select asp-for="AmountColumn" class="form-control column-dropdown" id="amount-column-select">
                    <option value="">— Select column —</option>
                </select>
            </div>

            <div class="form-group">
                <label asp-for="DescriptionColumn">Description Column</label>
                <select asp-for="DescriptionColumn" class="form-control column-dropdown" id="description-column-select">
                    <option value="">— Select column —</option>
                </select>
            </div>

            <div class="form-group">
                <label asp-for="CategoryColumn">Category Column <span class="form-optional">(optional)</span></label>
                <select asp-for="CategoryColumn" class="form-control column-dropdown" id="category-column-select">
                    <option value="">— None —</option>
                </select>
            </div>

            <div class="form-group form-check">
                <input asp-for="FlipDebitSign" type="checkbox" class="form-check-input" />
                <label asp-for="FlipDebitSign">My bank exports debits as negative numbers</label>
                <p class="form-hint">Check this if withdrawals appear as negative amounts in your file (e.g. -100.00).</p>
            </div>
        </div>
    </div>

    <div class="form-actions">
        <button type="submit" class="btn btn-primary" id="import-btn" disabled>
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="btn-icon"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
            Import
        </button>
        <a asp-controller="Transactions" asp-action="Index" class="btn btn-secondary">Cancel</a>
    </div>
</form>

<script>
(function () {
    const fileInput     = document.getElementById('import-file-input');
    const mappingSection = document.getElementById('column-mapping-section');
    const importBtn     = document.getElementById('import-btn');
    const dropdowns     = {
        date:        document.getElementById('date-column-select'),
        amount:      document.getElementById('amount-column-select'),
        description: document.getElementById('description-column-select'),
        category:    document.getElementById('category-column-select')
    };
    const profileSelect = document.getElementById('profile-select');
    const manualMapping = document.getElementById('manual-mapping');

    fileInput.addEventListener('change', async function () {
        const file = fileInput.files[0];
        if (!file) return;

        const form = new FormData();
        form.append('file', file);

        try {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const resp  = await fetch('/api/import/headers', {
                method: 'POST',
                headers: { 'RequestVerificationToken': token },
                body: form
            });

            if (!resp.ok) {
                mappingSection.style.display = 'block';
                importBtn.disabled = false;
                return;
            }

            const data = await resp.json();
            populateDropdowns(data);
            mappingSection.style.display = 'block';
            importBtn.disabled = false;
        } catch {
            mappingSection.style.display = 'block';
            importBtn.disabled = false;
        }
    });

    function populateDropdowns(data) {
        const headers = data.headers || [];
        ['date', 'amount', 'description', 'category'].forEach(field => {
            const sel = dropdowns[field];
            // Keep the "— None —" / "— Select column —" first option, clear the rest
            while (sel.options.length > 1) sel.remove(1);
            headers.forEach(h => {
                const opt = new Option(h, h);
                sel.add(opt);
            });
        });

        // Auto-select detected matches
        if (data.dateColumn)        setSelected(dropdowns.date,        data.dateColumn);
        if (data.amountColumn)      setSelected(dropdowns.amount,      data.amountColumn);
        if (data.descriptionColumn) setSelected(dropdowns.description, data.descriptionColumn);
        if (data.categoryColumn)    setSelected(dropdowns.category,    data.categoryColumn);
    }

    function setSelected(select, value) {
        for (let i = 0; i < select.options.length; i++) {
            if (select.options[i].value === value) {
                select.selectedIndex = i;
                return;
            }
        }
    }

    // Profile selection: hide manual mapping when a profile is chosen
    if (profileSelect) {
        profileSelect.addEventListener('change', function () {
            manualMapping.style.display = profileSelect.value ? 'none' : 'block';
        });
    }
})();
</script>
```

- [ ] **Step 2: Start the app and manually verify**

```bash
dotnet run --project ProjectCeres
```

Open `http://localhost:5000/import`. Verify:
- Only File + Account are visible initially
- Uploading the Sabadell XLSX reveals the column mapping section with Fecha/Importe/Concepto pre-selected
- Import button is disabled until a file is chosen
- Selecting a saved profile (if any) hides manual fields

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Import/Index.cshtml
git commit -m "feat: progressive disclosure import form — column mapping revealed after upload with auto-detected headers"
```

---

## Task 7: Add summary-card CSS modifier classes

**Files:**
- Modify: `ProjectCeres/Styles/app.css`

The `summary-card--success`, `--warning`, `--danger`, `--neutral`, and `--info` modifier classes are referenced in the Summary view but not defined in `app.css`. This task adds them.

- [ ] **Step 1: Find the import section in app.css and add the classes**

Open `ProjectCeres/Styles/app.css`. After the `/* ─── Dashboard ─────────────────────────────────────── */` section (around line 169), add:

```css
/* ─── Import Summary Cards ──────────────────────────── */

.summary-cards          { @apply grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6; }
.summary-card           { @apply flex flex-col items-center justify-center p-4 text-center; }
.summary-card__value    { @apply text-3xl font-bold mb-1; }
.summary-card__label    { @apply flex items-center gap-1 text-sm font-medium; }

.summary-card--success  { @apply border-green-200 bg-green-50; }
.summary-card--success .summary-card__value  { @apply text-green-700; }
.summary-card--success .summary-card__label  { @apply text-green-600; }

.summary-card--info     { @apply border-blue-200 bg-blue-50; }
.summary-card--info .summary-card__value     { @apply text-blue-700; }
.summary-card--info .summary-card__label     { @apply text-blue-600; }

.summary-card--warning  { @apply border-yellow-200 bg-yellow-50; }
.summary-card--warning .summary-card__value  { @apply text-yellow-700; }
.summary-card--warning .summary-card__label  { @apply text-yellow-600; }

.summary-card--danger   { @apply border-red-200 bg-red-50; }
.summary-card--danger .summary-card__value   { @apply text-red-700; }
.summary-card--danger .summary-card__label   { @apply text-red-600; }

.summary-card--neutral  { @apply border-gray-200 bg-gray-50; }
.summary-card--neutral .summary-card__value  { @apply text-gray-600; }
.summary-card--neutral .summary-card__label  { @apply text-gray-500; }
```

- [ ] **Step 2: Rebuild CSS**

```bash
dotnet build ProjectCeres
```

Expected: build succeeds, `wwwroot/css/site.css` regenerated.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Styles/app.css
git commit -m "feat: add summary-card CSS modifier classes for import summary view"
```

---

## Task 8: Update the Summary view — Reconciled card + save-profile prompt

**Files:**
- Modify: `ProjectCeres/Views/Import/Summary.cshtml`

- [ ] **Step 1: Replace Summary.cshtml**

```cshtml
@model ImportSummaryViewModel
@{ ViewData["Title"] = "Import Summary"; }

<div class="page-header">
    <h1>Import Summary</h1>
</div>

<div class="summary-cards">
    <div class="card summary-card summary-card--success">
        <div class="summary-card__value">@Model.RowsImported</div>
        <div class="summary-card__label">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
            Imported
        </div>
    </div>

    <div class="card summary-card summary-card--info">
        <div class="summary-card__value">@Model.RowsReconciled</div>
        <div class="summary-card__label">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
            Reconciled
        </div>
    </div>

    <div class="card summary-card summary-card--warning">
        <div class="summary-card__value">@Model.RowsFlagged</div>
        <div class="summary-card__label">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            Needs Review
        </div>
    </div>

    <div class="card summary-card @(Model.RowsFailed > 0 ? "summary-card--danger" : "summary-card--neutral")">
        <div class="summary-card__value">@Model.RowsFailed</div>
        <div class="summary-card__label">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            Failed
        </div>
    </div>
</div>

@if (Model.RowsFlagged > 0)
{
    <div class="alert alert-warning mt-4">
        <strong>@Model.RowsFlagged row(s) need categorization.</strong>
        These transactions were imported as <em>Uncategorized</em> because their category could not be determined.
        <a asp-controller="Transactions" asp-action="Index">Review them in Transactions</a>.
    </div>
}

@if (Model.Errors.Any())
{
    <h2 class="mt-6">Row Errors</h2>
    <div class="card">
        <ul class="error-list">
            @foreach (var error in Model.Errors)
            {
                <li>@error</li>
            }
        </ul>
    </div>
}

@if (Model.MappingsToSave is not null)
{
    <div class="card mt-6 p-4">
        <h2 class="text-lg font-semibold mb-1">Save these settings for next time?</h2>
        <p class="form-hint mb-4">Give this column mapping a name so you can reuse it on your next import.</p>

        <form asp-action="SaveProfile" method="post" class="form-standard">
            @Html.AntiForgeryToken()
            <input type="hidden" name="dateColumn"        value="@Model.MappingsToSave.DateColumn" />
            <input type="hidden" name="amountColumn"      value="@Model.MappingsToSave.AmountColumn" />
            <input type="hidden" name="descriptionColumn" value="@Model.MappingsToSave.DescriptionColumn" />
            <input type="hidden" name="categoryColumn"    value="@Model.MappingsToSave.CategoryColumn" />
            <input type="hidden" name="flipDebitSign"     value="@Model.MappingsToSave.FlipDebitSign.ToString().ToLower()" />

            <div class="form-group" style="max-width: 320px;">
                <label for="profileName">Profile Name</label>
                <input type="text" id="profileName" name="profileName" class="form-control"
                       placeholder="e.g. Sabadell Checking" required />
            </div>

            <div class="form-actions">
                <button type="submit" class="btn btn-primary">Save Profile</button>
                <a asp-action="Index" class="btn btn-secondary">Skip</a>
            </div>
        </form>
    </div>
}
else
{
    <div class="form-actions mt-6">
        <a asp-controller="Transactions" asp-action="Index" class="btn btn-primary">View Transactions</a>
        <a asp-action="Index" class="btn btn-secondary">Import Another File</a>
    </div>
}
```

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test ProjectCeres.Tests --logger "console;verbosity=normal"
```

Expected: all tests pass.

- [ ] **Step 3: Manually verify end-to-end**

```bash
dotnet run --project ProjectCeres
```

Import a file. Verify the Summary shows four cards (Imported, Reconciled, Needs Review, Failed). Import again with the same file — verify Reconciled count increases and no duplicates appear in Transactions.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Import/Summary.cshtml
git commit -m "feat: import summary — add Reconciled card and save-profile prompt"
```

---

## Self-Review Checklist

**Spec coverage:**
- [x] Progressive disclosure form — Task 6
- [x] Column dropdowns with auto-detection — Tasks 2, 3, 6
- [x] Saved profile dropdown hidden when no profiles — Task 6
- [x] Remove default category field — Tasks 4, 5
- [x] Uncategorized Income / Expense fallback — Tasks 1, 4
- [x] Reconciliation pass (match → clear, no duplicate) — Task 4
- [x] ±1 day date tolerance — Task 4
- [x] Reconciled count on summary — Tasks 4, 5, 7
- [x] Save-profile prompt after successful manual import — Tasks 5, 7
- [x] `summary-card--info` CSS class — added in Task 7 along with all other summary-card modifiers

---

## Note: Plan B

Transfer staging (the `ImportStagedTransfer` table, intra-file pairing, Transfer Review screen, `ImportTransferExclusion` training store) is a separate plan: `2026-04-26-import-ux-redesign-plan-b.md`. Plan A must be fully working before Plan B begins.
