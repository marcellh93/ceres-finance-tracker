# Import Transfer Staging (Plan B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add automatic transfer detection and staging to the import pipeline — rows that look like inter-account transfers are held for manual review instead of being imported as plain transactions.

**Architecture:** Two new DB tables (`ImportStagedTransfer`, `ImportTransferExclusion`) plus a `ITransferDetectionService` that runs a two-pass detection algorithm inside `ImportService.ImportAsync`. A new `TransferReviewController` serves the review screen where the user resolves each staged row. A persistent nav badge shows the pending count.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core + PostgreSQL (Npgsql), xUnit + Moq + FluentAssertions, Tailwind CSS v3 (Razor), PNPM/Vite (React client for nav badge).

---

## File Map

**New files:**
- `ProjectCeres/Models/ImportStagedTransfer.cs` — EF entity
- `ProjectCeres/Models/ImportTransferExclusion.cs` — EF entity
- `ProjectCeres/Models/StagedTransferStatus.cs` — enum (Pending, Linked, CreatedAsTransfer, DismissedAsTransaction)
- `ProjectCeres/Services/ITransferDetectionService.cs` — interface
- `ProjectCeres/Services/TransferDetectionService.cs` — detection logic (intra-file + cross-account + exclusion check)
- `ProjectCeres/Services/ITransferReviewService.cs` — interface
- `ProjectCeres/Services/TransferReviewService.cs` — resolve actions (link, create, dismiss)
- `ProjectCeres/Controllers/TransferReviewController.cs` — MVC controller
- `ProjectCeres/ViewModels/StagedTransferViewModel.cs` — view model for review screen
- `ProjectCeres/Views/TransferReview/Index.cshtml` — list of pending staged rows
- `ProjectCeres.Tests/Unit/TransferDetectionServiceTests.cs` — unit tests for detection logic
- `ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs` — integration tests for resolve actions
- `ProjectCeres.Tests/Integration/TransferStagingImportTests.cs` — end-to-end import tests

**Modified files:**
- `ProjectCeres/Data/AppDbContext.cs` — add `DbSet<ImportStagedTransfer>`, `DbSet<ImportTransferExclusion>`, relationships
- `ProjectCeres/Services/ImportService.cs` — inject `ITransferDetectionService`; call detection before reconciliation; add `RowsStaged` to result
- `ProjectCeres/ViewModels/ImportResult.cs` — add `RowsStaged` property
- `ProjectCeres/ViewModels/ImportSummaryViewModel.cs` — add `RowsStaged` property
- `ProjectCeres/Controllers/ImportController.cs` — pass `RowsStaged` through TempData; pass it to summary VM
- `ProjectCeres/Views/Import/Summary.cshtml` — add "Staged for Transfer Review" count card + link
- `ProjectCeres/Views/Shared/_Layout.cshtml` — add pending staged transfer badge to nav
- `ProjectCeres/Program.cs` — register new services
- `ProjectCeres/Migrations/` — new migration for the two new tables

---

## Task 1: Add new models and enum

**Files:**
- Create: `ProjectCeres/Models/StagedTransferStatus.cs`
- Create: `ProjectCeres/Models/ImportStagedTransfer.cs`
- Create: `ProjectCeres/Models/ImportTransferExclusion.cs`

- [ ] **Step 1: Create the enum**

```csharp
// ProjectCeres/Models/StagedTransferStatus.cs
namespace ProjectCeres.Models;

public enum StagedTransferStatus
{
    Pending,
    Linked,
    CreatedAsTransfer,
    DismissedAsTransaction
}
```

- [ ] **Step 2: Create ImportStagedTransfer**

```csharp
// ProjectCeres/Models/ImportStagedTransfer.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class ImportStagedTransfer
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly RawDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid? CandidateTransactionId { get; set; }
    public StagedTransferStatus Status { get; set; } = StagedTransferStatus.Pending;
    public DateTime? ResolvedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Transaction? CandidateTransaction { get; set; }
}
```

- [ ] **Step 3: Create ImportTransferExclusion**

```csharp
// ProjectCeres/Models/ImportTransferExclusion.cs
namespace ProjectCeres.Models;

public class ImportTransferExclusion
{
    public Guid Id { get; set; }
    public string DescriptionPattern { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Models/StagedTransferStatus.cs \
        ProjectCeres/Models/ImportStagedTransfer.cs \
        ProjectCeres/Models/ImportTransferExclusion.cs
git commit -m "feat: add ImportStagedTransfer, ImportTransferExclusion models and StagedTransferStatus enum"
```

---

## Task 2: Register new models in AppDbContext

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Add DbSets and configure relationships**

In `AppDbContext.cs`, add two `DbSet` properties after the existing `ImportProfiles` line:

```csharp
public DbSet<ImportStagedTransfer> ImportStagedTransfers => Set<ImportStagedTransfer>();
public DbSet<ImportTransferExclusion> ImportTransferExclusions => Set<ImportTransferExclusion>();
```

In `ConfigureRelationships`, add after the `ImportProfile` block:

```csharp
modelBuilder.Entity<ImportStagedTransfer>(entity =>
{
    entity.ToTable("ImportStagedTransfers");
    entity.Property(e => e.Status)
          .HasConversion<string>()
          .HasMaxLength(30);

    entity.HasOne(e => e.Account)
          .WithMany()
          .HasForeignKey(e => e.AccountId)
          .OnDelete(DeleteBehavior.Restrict);

    entity.HasOne(e => e.CandidateTransaction)
          .WithMany()
          .HasForeignKey(e => e.CandidateTransactionId)
          .OnDelete(DeleteBehavior.SetNull);
});

modelBuilder.Entity<ImportTransferExclusion>(entity =>
{
    entity.ToTable("ImportTransferExclusions");
    entity.HasIndex(e => e.DescriptionPattern).IsUnique();
});
```

- [ ] **Step 2: Create and apply migration**

```bash
cd <repo>
dotnet ef migrations add AddTransferStagingTables --project ProjectCeres
dotnet ef database update --project ProjectCeres
```

Expected: migration file created, `dotnet ef database update` exits with 0. Verify with `dotnet ef migrations list --project ProjectCeres` — new migration should be at the top marked `[applied]`.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Data/AppDbContext.cs \
        ProjectCeres/Migrations/
git commit -m "feat: register ImportStagedTransfer and ImportTransferExclusion in AppDbContext, add migration"
```

---

## Task 3: Add RowsStaged to ImportResult and ImportSummaryViewModel

**Files:**
- Modify: `ProjectCeres/ViewModels/ImportResult.cs`
- Modify: `ProjectCeres/ViewModels/ImportSummaryViewModel.cs`

- [ ] **Step 1: Add RowsStaged to ImportResult**

Open `ProjectCeres/ViewModels/ImportResult.cs`. The current content is:

```csharp
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

Change it to:

```csharp
namespace ProjectCeres.ViewModels;

public class ImportResult
{
    public int RowsImported    { get; set; }
    public int RowsReconciled  { get; set; }
    public int RowsFlagged     { get; set; }
    public int RowsStaged      { get; set; }
    public int RowsFailed      { get; set; }
    public List<string> Errors { get; set; } = [];
}
```

- [ ] **Step 2: Add RowsStaged to ImportSummaryViewModel**

Open `ProjectCeres/ViewModels/ImportSummaryViewModel.cs`. Change it to:

```csharp
namespace ProjectCeres.ViewModels;

public class ImportSummaryViewModel
{
    public int RowsImported   { get; set; }
    public int RowsReconciled { get; set; }
    public int RowsFlagged    { get; set; }
    public int RowsStaged     { get; set; }
    public int RowsFailed     { get; set; }
    public List<string> Errors { get; set; } = [];

    // Set when import used manual column mappings (no saved profile) — offer to save
    public ImportColumnMappings? MappingsToSave { get; set; }

    public int TotalProcessed => RowsImported + RowsReconciled + RowsFlagged + RowsStaged + RowsFailed;
}
```

- [ ] **Step 3: Build to confirm no compile errors**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/ViewModels/ImportResult.cs \
        ProjectCeres/ViewModels/ImportSummaryViewModel.cs
git commit -m "feat: add RowsStaged to ImportResult and ImportSummaryViewModel"
```

---

## Task 4: TransferDetectionService — write failing tests first

**Files:**
- Create: `ProjectCeres.Tests/Unit/TransferDetectionServiceTests.cs`

The detection service will receive parsed rows plus existing transactions (from other accounts) and exclusion patterns. It returns two outputs: a list of row indices to stage (removed from regular import) and a list of `ImportStagedTransfer` entities to insert.

- [ ] **Step 1: Write the failing tests**

```csharp
// ProjectCeres.Tests/Unit/TransferDetectionServiceTests.cs
using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class TransferDetectionServiceTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2024, 3, 15);

    private static ParsedImportRow Row(decimal amount, string desc = "Payment", DateOnly? date = null) => new()
    {
        Date        = date ?? Today,
        Amount      = amount,
        Description = desc
    };

    // ── Intra-file pairing ────────────────────────────────────────────────────

    [Fact]
    public void Detect_IntraFilePair_StagesBothRows()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Transfer out"),
            Row(-100m, "Transfer in"),
        };

        var result = service.Detect(rows, existingCrossAccountTxns: [], exclusionPatterns: [], accountId: AccountId);

        result.StagedRows.Should().HaveCount(2);
        result.StagedRows.Select(s => s.RawAmount).Should().BeEquivalentTo(new[] { 100m, -100m });
    }

    [Fact]
    public void Detect_IntraFilePair_NeitherRowPassedToImport()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Transfer out"),
            Row(-100m, "Transfer in"),
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void Detect_IntraFilePair_SameAmountDifferentDate_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  date: Today),
            Row(-100m, date: Today.AddDays(5)),
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.StagedRows.Should().BeEmpty();
        result.RowIndicesToSkip.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ThreeRowsSameAmount_OnlyFirstPairStaged()
    {
        // If three rows match, only pair the first two — third is a plain transaction
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Out 1"),
            Row(-100m, "In 1"),
            Row(100m,  "Out 2"), // no negative partner left
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.StagedRows.Should().HaveCount(2);
        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    // ── Cross-account pairing ──────────────────────────────────────────────────

    [Fact]
    public void Detect_CrossAccountMatch_StagedWithCandidateId()
    {
        var service = new TransferDetectionService();
        var candidateId = Guid.NewGuid();
        var rows = new List<ParsedImportRow> { Row(-200m, "Wire transfer") };

        var crossAccountTxn = new Transaction
        {
            Id        = candidateId,
            Date      = Today,
            Amount    = 200m,
            AccountId = Guid.NewGuid()
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().HaveCount(1);
        result.StagedRows[0].CandidateTransactionId.Should().Be(candidateId);
        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void Detect_CrossAccountMatch_WithinOneDayTolerance_Staged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(-50m, "Wire", Today) };
        var crossAccountTxn = new Transaction
        {
            Id     = Guid.NewGuid(),
            Date   = Today.AddDays(1),
            Amount = 50m
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().HaveCount(1);
    }

    [Fact]
    public void Detect_CrossAccountMatch_BeyondOneDayTolerance_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(-50m, "Wire", Today) };
        var crossAccountTxn = new Transaction
        {
            Id     = Guid.NewGuid(),
            Date   = Today.AddDays(2),
            Amount = 50m
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().BeEmpty();
    }

    // ── Exclusion / training store ─────────────────────────────────────────────

    [Fact]
    public void Detect_DescriptionMatchesExclusion_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "bizum payment"),
            Row(-100m, "bizum payment"),
        };

        var result = service.Detect(rows, [], exclusionPatterns: ["bizum"], AccountId);

        result.StagedRows.Should().BeEmpty();
        result.RowIndicesToSkip.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ExclusionCaseInsensitive_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "BIZUM PAYMENT"),
            Row(-100m, "Bizum Transfer"),
        };

        var result = service.Detect(rows, [], ["bizum"], AccountId);

        result.StagedRows.Should().BeEmpty();
    }

    // ── Staged entity shape ────────────────────────────────────────────────────

    [Fact]
    public void Detect_StagedRow_HasCorrectFields()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(77m, "Transfer ABC"), Row(-77m, "Transfer XYZ") };

        var result = service.Detect(rows, [], [], AccountId);

        var staged = result.StagedRows[0];
        staged.AccountId.Should().Be(AccountId);
        staged.Status.Should().Be(StagedTransferStatus.Pending);
        staged.ResolvedAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd <repo>
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Unit.TransferDetectionServiceTests" 2>&1 | tail -20
```

Expected: FAIL — `TransferDetectionService` not found / `ITransferDetectionService` not found.

- [ ] **Step 3: Commit failing tests**

```bash
git add ProjectCeres.Tests/Unit/TransferDetectionServiceTests.cs
git commit -m "test: add failing unit tests for TransferDetectionService"
```

---

## Task 5: Implement TransferDetectionService

**Files:**
- Create: `ProjectCeres/Services/ITransferDetectionService.cs`
- Create: `ProjectCeres/Services/TransferDetectionService.cs`

- [ ] **Step 1: Define the result type and interface**

```csharp
// ProjectCeres/Services/ITransferDetectionService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public record TransferDetectionResult(
    IReadOnlyList<ImportStagedTransfer> StagedRows,
    IReadOnlySet<int> RowIndicesToSkip);

public interface ITransferDetectionService
{
    TransferDetectionResult Detect(
        IReadOnlyList<ParsedImportRow> rows,
        IReadOnlyList<Transaction> existingCrossAccountTxns,
        IReadOnlyList<string> exclusionPatterns,
        Guid accountId);
}
```

- [ ] **Step 2: Implement TransferDetectionService**

```csharp
// ProjectCeres/Services/TransferDetectionService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransferDetectionService : ITransferDetectionService
{
    private const int DateToleranceDays = 1;

    public TransferDetectionResult Detect(
        IReadOnlyList<ParsedImportRow> rows,
        IReadOnlyList<Transaction> existingCrossAccountTxns,
        IReadOnlyList<string> exclusionPatterns,
        Guid accountId)
    {
        var staged      = new List<ImportStagedTransfer>();
        var skipIndices = new HashSet<int>();

        var normalizedExclusions = exclusionPatterns
            .Select(p => p.ToLowerInvariant())
            .ToList();

        // Track which intra-file indices have already been paired
        var pairedIndices = new HashSet<int>();

        for (int i = 0; i < rows.Count; i++)
        {
            if (pairedIndices.Contains(i)) continue;

            var row = rows[i];

            // Check exclusion first
            if (IsExcluded(row.Description, normalizedExclusions))
                continue;

            // Pass 1a: intra-file pairing
            int partnerIdx = FindIntraFilePartner(rows, i, pairedIndices, normalizedExclusions);
            if (partnerIdx >= 0)
            {
                pairedIndices.Add(i);
                pairedIndices.Add(partnerIdx);
                skipIndices.Add(i);
                skipIndices.Add(partnerIdx);

                staged.Add(MakeStagedRow(row, accountId, candidateTransactionId: null));
                staged.Add(MakeStagedRow(rows[partnerIdx], accountId, candidateTransactionId: null));
                continue;
            }

            // Pass 1b: cross-account pairing
            var crossMatch = FindCrossAccountMatch(row, existingCrossAccountTxns);
            if (crossMatch is not null)
            {
                skipIndices.Add(i);
                staged.Add(MakeStagedRow(row, accountId, candidateTransactionId: crossMatch.Id));
            }
        }

        return new TransferDetectionResult(staged.AsReadOnly(), skipIndices);
    }

    private static int FindIntraFilePartner(
        IReadOnlyList<ParsedImportRow> rows,
        int sourceIdx,
        HashSet<int> alreadyPaired,
        List<string> normalizedExclusions)
    {
        var source = rows[sourceIdx];
        for (int j = sourceIdx + 1; j < rows.Count; j++)
        {
            if (alreadyPaired.Contains(j)) continue;
            var candidate = rows[j];
            if (IsExcluded(candidate.Description, normalizedExclusions)) continue;
            if (candidate.Amount == -source.Amount && candidate.Date == source.Date)
                return j;
        }
        return -1;
    }

    private static Transaction? FindCrossAccountMatch(
        ParsedImportRow row,
        IReadOnlyList<Transaction> crossAccountTxns)
    {
        var absAmount = Math.Abs(row.Amount);
        return crossAccountTxns.FirstOrDefault(t =>
            t.Amount == absAmount &&
            Math.Abs((t.Date.ToDateTime(TimeOnly.MinValue) -
                      row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= DateToleranceDays);
    }

    private static bool IsExcluded(string? description, List<string> normalizedExclusions)
    {
        if (description is null) return false;
        var lower = description.ToLowerInvariant();
        return normalizedExclusions.Any(p => lower.Contains(p));
    }

    private static ImportStagedTransfer MakeStagedRow(
        ParsedImportRow row,
        Guid accountId,
        Guid? candidateTransactionId) => new()
    {
        Id                      = Guid.NewGuid(),
        ImportedAt              = DateTime.UtcNow,
        AccountId               = accountId,
        RawDate                 = row.Date,
        RawAmount               = row.Amount,
        RawDescription          = row.Description,
        CandidateTransactionId  = candidateTransactionId,
        Status                  = StagedTransferStatus.Pending
    };
}
```

- [ ] **Step 3: Run the tests — verify they pass**

```bash
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Unit.TransferDetectionServiceTests" 2>&1 | tail -20
```

Expected: all tests PASS, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/ITransferDetectionService.cs \
        ProjectCeres/Services/TransferDetectionService.cs
git commit -m "feat: implement TransferDetectionService with intra-file pairing, cross-account matching, and exclusion check"
```

---

## Task 6: Integrate detection into ImportService

**Files:**
- Modify: `ProjectCeres/Services/ImportService.cs`
- Modify: `ProjectCeres/Services/IImportService.cs` (add RowsStaged — already added via ImportResult)
- Modify: `ProjectCeres/Program.cs` — register TransferDetectionService

- [ ] **Step 1: Write integration test first**

Create `ProjectCeres.Tests/Integration/TransferStagingImportTests.cs`:

```csharp
// ProjectCeres.Tests/Integration/TransferStagingImportTests.cs
using FluentAssertions;
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

        var accountService          = new AccountService(_fixture.Db);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService);
        var attachmentService       = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentService);
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
        // Seed an existing transaction on account B
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
        // Seed an exclusion
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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Integration.TransferStagingImportTests" 2>&1 | tail -20
```

Expected: compile or runtime error — `ImportService` constructor doesn't accept `TransferDetectionService` yet.

- [ ] **Step 3: Update ImportService to inject and call detection**

Replace the content of `ProjectCeres/Services/ImportService.cs` with:

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
    ITransactionService? transactionService = null,
    ITransferDetectionService? transferDetectionService = null) : IImportService
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

        // Load exclusion patterns for transfer detection
        var exclusionPatterns = transferDetectionService is not null
            ? await db.ImportTransferExclusions
                .Select(e => e.DescriptionPattern)
                .ToListAsync()
            : (IReadOnlyList<string>)[];

        // Load transactions from other accounts for cross-account matching
        var crossAccountTxns = transferDetectionService is not null
            ? await db.Transactions
                .Where(t => t.AccountId != accountId)
                .ToListAsync()
            : (IReadOnlyList<Transaction>)[];

        // Run transfer detection pass
        HashSet<int> skipIndices = [];
        if (transferDetectionService is not null)
        {
            var detection = transferDetectionService.Detect(rows, crossAccountTxns, exclusionPatterns, accountId);

            foreach (var staged in detection.StagedRows)
                db.ImportStagedTransfers.Add(staged);

            await db.SaveChangesAsync();

            skipIndices = detection.RowIndicesToSkip.ToHashSet();
            result.RowsStaged = detection.StagedRows.Count;
        }

        var existingTxns = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .ToListAsync();

        for (int i = 0; i < rows.Count; i++)
        {
            if (skipIndices.Contains(i)) continue;

            var row = rows[i];
            try
            {
                // Reconciliation pass: match by amount + date ±1 day
                var match = existingTxns.FirstOrDefault(e =>
                    e.Amount == Math.Abs(row.Amount) &&
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
                    Amount      = Math.Abs(row.Amount),
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

- [ ] **Step 4: Register TransferDetectionService in Program.cs**

Open `ProjectCeres/Program.cs`. After the line `builder.Services.AddScoped<IImportService, ImportService>();`, add:

```csharp
builder.Services.AddScoped<ITransferDetectionService, TransferDetectionService>();
```

- [ ] **Step 5: Run all import integration tests**

```bash
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Integration.TransferStagingImportTests" 2>&1 | tail -30
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Integration.ImportServiceIntegrationTests" 2>&1 | tail -20
```

Expected: All tests PASS. (The existing ImportServiceIntegrationTests must still pass — they pass `null` for `transferDetectionService` via the existing optional-param path, so no staged rows are written, behaviour unchanged.)

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/ImportService.cs \
        ProjectCeres/Program.cs \
        ProjectCeres.Tests/Integration/TransferStagingImportTests.cs
git commit -m "feat: integrate TransferDetectionService into ImportAsync — staged rows skip reconciliation and transaction creation"
```

---

## Task 7: TransferReviewService — resolve actions

**Files:**
- Create: `ProjectCeres/Services/ITransferReviewService.cs`
- Create: `ProjectCeres/Services/TransferReviewService.cs`
- Create: `ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs`

- [ ] **Step 1: Write failing integration tests**

```csharp
// ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs
using FluentAssertions;
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

        var accountService          = new AccountService(_fixture.Db);
        var liabilityPaymentService = new LiabilityPaymentService(_fixture.Db, accountService);
        var attachmentMock          = new Mock<IFileAttachmentService>().Object;
        var transactionService      = new TransactionService(
            _fixture.Db, accountService, liabilityPaymentService, attachmentMock);
        var transferService         = new TransferService(_fixture.Db, accountService);

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

        // Should not throw on unique constraint
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
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Integration.TransferReviewServiceTests" 2>&1 | tail -20
```

Expected: FAIL — `TransferReviewService` not found.

- [ ] **Step 3: Define interface**

```csharp
// ProjectCeres/Services/ITransferReviewService.cs
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ITransferReviewService
{
    Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    /// <summary>Link staged row to an existing transaction on the other side, creating a Transfer record.</summary>
    Task LinkToExistingAsync(Guid stagedId, Guid otherAccountId);
    /// <summary>User specifies which account the other side belongs to. Creates Transfer + plain transaction on other side.</summary>
    Task CreateAsTransferAsync(Guid stagedId, Guid otherAccountId);
    /// <summary>Import staged row as a plain transaction; save description to exclusion store.</summary>
    Task DismissAsTransactionAsync(Guid stagedId);
}
```

- [ ] **Step 4: Implement TransferReviewService**

```csharp
// ProjectCeres/Services/TransferReviewService.cs
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransferReviewService(
    AppDbContext db,
    ITransferService transferService,
    ITransactionService transactionService) : ITransferReviewService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public async Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync() =>
        await db.ImportStagedTransfers
            .Include(s => s.Account)
            .Include(s => s.CandidateTransaction)
            .Where(s => s.Status == StagedTransferStatus.Pending)
            .OrderBy(s => s.ImportedAt)
            .ToListAsync();

    public async Task<int> GetPendingCountAsync() =>
        await db.ImportStagedTransfers
            .CountAsync(s => s.Status == StagedTransferStatus.Pending);

    public async Task LinkToExistingAsync(Guid stagedId, Guid otherAccountId)
    {
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId)
            ?? throw new InvalidOperationException($"Staged transfer {stagedId} not found.");

        var absAmount = Math.Abs(staged.RawAmount);
        var (sourceId, destId) = staged.RawAmount < 0
            ? (staged.AccountId, otherAccountId)
            : (otherAccountId, staged.AccountId);

        await transferService.CreateAsync(new TransferCreateViewModel
        {
            Date          = staged.RawDate,
            Amount        = absAmount,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            Description   = staged.RawDescription
        });

        staged.Status     = StagedTransferStatus.Linked;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CreateAsTransferAsync(Guid stagedId, Guid otherAccountId)
    {
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId)
            ?? throw new InvalidOperationException($"Staged transfer {stagedId} not found.");

        var absAmount = Math.Abs(staged.RawAmount);
        var (sourceId, destId) = staged.RawAmount < 0
            ? (staged.AccountId, otherAccountId)
            : (otherAccountId, staged.AccountId);

        await transferService.CreateAsync(new TransferCreateViewModel
        {
            Date            = staged.RawDate,
            Amount          = absAmount,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            Description     = staged.RawDescription
        });

        staged.Status     = StagedTransferStatus.CreatedAsTransfer;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task DismissAsTransactionAsync(Guid stagedId)
    {
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId)
            ?? throw new InvalidOperationException($"Staged transfer {stagedId} not found.");

        var categoryId = staged.RawAmount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;

        var txId = await transactionService.CreateAsync(new TransactionCreateViewModel
        {
            Date        = staged.RawDate,
            Amount      = Math.Abs(staged.RawAmount),
            Description = staged.RawDescription,
            AccountId   = staged.AccountId,
            CategoryId  = categoryId
        });
        await transactionService.MarkClearedAsync(txId, cleared: true);
        await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);

        // Save exclusion pattern if description is non-empty and not already stored
        if (!string.IsNullOrWhiteSpace(staged.RawDescription))
        {
            var normalized = staged.RawDescription.Trim();
            var exists = await db.ImportTransferExclusions
                .AnyAsync(e => e.DescriptionPattern == normalized);
            if (!exists)
            {
                db.ImportTransferExclusions.Add(new ImportTransferExclusion
                {
                    Id                 = Guid.NewGuid(),
                    DescriptionPattern = normalized,
                    CreatedAt          = DateTime.UtcNow
                });
            }
        }

        staged.Status     = StagedTransferStatus.DismissedAsTransaction;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Register in Program.cs**

After the `ITransferDetectionService` line, add:

```csharp
builder.Services.AddScoped<ITransferReviewService, TransferReviewService>();
```

- [ ] **Step 6: Run tests to confirm they pass**

```bash
dotnet test ProjectCeres.Tests --filter "Class=ProjectCeres.Tests.Integration.TransferReviewServiceTests" 2>&1 | tail -30
```

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/ITransferReviewService.cs \
        ProjectCeres/Services/TransferReviewService.cs \
        ProjectCeres/Program.cs \
        ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs
git commit -m "feat: implement TransferReviewService with link, create-as-transfer, and dismiss actions"
```

---

## Task 8: TransferReviewController and Views

**Files:**
- Create: `ProjectCeres/ViewModels/StagedTransferViewModel.cs`
- Create: `ProjectCeres/Controllers/TransferReviewController.cs`
- Create: `ProjectCeres/Views/TransferReview/Index.cshtml`

- [ ] **Step 1: Create view model**

```csharp
// ProjectCeres/ViewModels/StagedTransferViewModel.cs
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class StagedTransferViewModel
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly RawDate { get; set; }
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid? CandidateTransactionId { get; set; }
    public string? CandidateTransactionDescription { get; set; }
    public DateOnly? CandidateTransactionDate { get; set; }
    public decimal? CandidateTransactionAmount { get; set; }
}
```

- [ ] **Step 2: Create the controller**

```csharp
// ProjectCeres/Controllers/TransferReviewController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class TransferReviewController(
    ITransferReviewService reviewService,
    AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var pending = await reviewService.GetPendingAsync();

        var vms = pending.Select(s => new StagedTransferViewModel
        {
            Id                               = s.Id,
            ImportedAt                       = s.ImportedAt,
            AccountName                      = s.Account?.Name ?? s.AccountId.ToString(),
            RawDate                          = s.RawDate,
            RawAmount                        = s.RawAmount,
            RawDescription                   = s.RawDescription,
            CandidateTransactionId           = s.CandidateTransactionId,
            CandidateTransactionDescription  = s.CandidateTransaction?.Description,
            CandidateTransactionDate         = s.CandidateTransaction?.Date,
            CandidateTransactionAmount       = s.CandidateTransaction?.Amount
        }).ToList();

        ViewBag.Accounts = new SelectList(
            await db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(),
            "Id", "Name");

        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkToExisting(Guid stagedId, Guid otherAccountId)
    {
        try
        {
            await reviewService.LinkToExistingAsync(stagedId, otherAccountId);
            TempData["SuccessMessage"] = "Transfer linked successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAsTransfer(Guid stagedId, Guid otherAccountId)
    {
        try
        {
            await reviewService.CreateAsTransferAsync(stagedId, otherAccountId);
            TempData["SuccessMessage"] = "Transfer created successfully.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissAsTransaction(Guid stagedId)
    {
        try
        {
            await reviewService.DismissAsTransactionAsync(stagedId);
            TempData["SuccessMessage"] = "Row imported as a plain transaction.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 3: Create the Transfer Review index view**

```cshtml
@* ProjectCeres/Views/TransferReview/Index.cshtml *@
@model IEnumerable<StagedTransferViewModel>
@{ ViewData["Title"] = "Transfer Review"; }

<div class="page-header">
    <h1>Transfer Review</h1>
    <p class="text-sm text-gray-500">These rows look like transfers between accounts. Resolve each one before they appear as transactions.</p>
</div>

@if (!Model.Any())
{
    <div class="card mt-4">
        <p class="text-gray-500 text-sm">No pending transfers to review.</p>
        <div class="mt-4">
            <a asp-controller="Transactions" asp-action="Index" class="btn btn-secondary">View Transactions</a>
        </div>
    </div>
}
else
{
    foreach (var row in Model)
    {
        <div class="card mt-4 p-4">
            <div class="flex items-start justify-between gap-4 flex-wrap">
                <div>
                    <div class="text-sm text-gray-500 mb-1">@row.AccountName — imported @row.ImportedAt.ToString("dd/MM/yyyy HH:mm")</div>
                    <div class="font-semibold text-gray-800">@row.RawDate.ToString("dd/MM/yyyy") &nbsp;
                        <span class="@(row.RawAmount < 0 ? "text-red-600" : "text-green-700")">
                            @row.RawAmount.ToString("F2")
                        </span>
                    </div>
                    @if (!string.IsNullOrEmpty(row.RawDescription))
                    {
                        <div class="text-sm text-gray-600 mt-0.5">@row.RawDescription</div>
                    }

                    @if (row.CandidateTransactionId.HasValue)
                    {
                        <div class="mt-2 text-sm text-blue-700 bg-blue-50 rounded px-2 py-1 inline-block">
                            Possible match: @row.CandidateTransactionDate?.ToString("dd/MM/yyyy")
                            @row.CandidateTransactionAmount?.ToString("F2")
                            @row.CandidateTransactionDescription
                        </div>
                    }
                </div>
            </div>

            <div class="mt-4 flex flex-wrap gap-3 items-end">
                @if (row.CandidateTransactionId.HasValue)
                {
                    <form asp-action="LinkToExisting" method="post" class="flex items-end gap-2">
                        @Html.AntiForgeryToken()
                        <input type="hidden" name="stagedId" value="@row.Id" />
                        <div class="form-group mb-0">
                            <label class="text-xs text-gray-600 block mb-1">Link to existing — other account</label>
                            <select name="otherAccountId" class="form-control text-sm" required>
                                <option value="">— Select account —</option>
                                @foreach (var item in (ViewBag.Accounts as Microsoft.AspNetCore.Mvc.Rendering.SelectList)!)
                                {
                                    <option value="@item.Value">@item.Text</option>
                                }
                            </select>
                        </div>
                        <button type="submit" class="btn btn-primary text-sm">Link Transfer</button>
                    </form>
                }

                <form asp-action="CreateAsTransfer" method="post" class="flex items-end gap-2">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="stagedId" value="@row.Id" />
                    <div class="form-group mb-0">
                        <label class="text-xs text-gray-600 block mb-1">Specify other account</label>
                        <select name="otherAccountId" class="form-control text-sm" required>
                            <option value="">— Select account —</option>
                            @foreach (var item in (ViewBag.Accounts as Microsoft.AspNetCore.Mvc.Rendering.SelectList)!)
                            {
                                <option value="@item.Value">@item.Text</option>
                            }
                        </select>
                    </div>
                    <button type="submit" class="btn btn-primary text-sm">Create Transfer</button>
                </form>

                <form asp-action="DismissAsTransaction" method="post">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="stagedId" value="@row.Id" />
                    <button type="submit" class="btn btn-danger text-sm">Not a Transfer</button>
                </form>
            </div>
        </div>
    }
}
```

- [ ] **Step 4: Build to confirm no compile errors**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/ViewModels/StagedTransferViewModel.cs \
        ProjectCeres/Controllers/TransferReviewController.cs \
        ProjectCeres/Views/TransferReview/Index.cshtml
git commit -m "feat: add TransferReviewController and Index view"
```

---

## Task 9: Update ImportController and Summary view

**Files:**
- Modify: `ProjectCeres/Controllers/ImportController.cs`
- Modify: `ProjectCeres/Views/Import/Summary.cshtml`

- [ ] **Step 1: Pass RowsStaged through TempData in ImportController**

In `ProjectCeres/Controllers/ImportController.cs`, in the `Index` POST action, find the block that sets TempData after a successful import. It currently reads:

```csharp
TempData["ImportRowsImported"]   = result.RowsImported;
TempData["ImportRowsReconciled"] = result.RowsReconciled;
TempData["ImportRowsFlagged"]    = result.RowsFlagged;
TempData["ImportRowsFailed"]     = result.RowsFailed;
TempData["ImportErrors"]         = string.Join("\n", result.Errors);
```

Change it to:

```csharp
TempData["ImportRowsImported"]   = result.RowsImported;
TempData["ImportRowsReconciled"] = result.RowsReconciled;
TempData["ImportRowsFlagged"]    = result.RowsFlagged;
TempData["ImportRowsStaged"]     = result.RowsStaged;
TempData["ImportRowsFailed"]     = result.RowsFailed;
TempData["ImportErrors"]         = string.Join("\n", result.Errors);
```

In the `Summary()` action, find where the VM is built. Currently:

```csharp
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
```

Change it to:

```csharp
var vm = new ImportSummaryViewModel
{
    RowsImported   = (int)(TempData["ImportRowsImported"]   ?? 0),
    RowsReconciled = (int)(TempData["ImportRowsReconciled"] ?? 0),
    RowsFlagged    = (int)(TempData["ImportRowsFlagged"]    ?? 0),
    RowsStaged     = (int)(TempData["ImportRowsStaged"]     ?? 0),
    RowsFailed     = (int)(TempData["ImportRowsFailed"]     ?? 0),
    Errors         = ((string?)TempData["ImportErrors"] ?? string.Empty)
                      .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                      .ToList()
};
```

- [ ] **Step 2: Add Staged count card to Summary view**

Open `ProjectCeres/Views/Import/Summary.cshtml`. Add the new card after the Reconciled card (after `</div>` closing `summary-card--info`) and before the warning card block. Insert:

```cshtml
<div class="card summary-card @(Model.RowsStaged > 0 ? "summary-card--warning" : "summary-card--neutral")">
    <div class="summary-card__value">@Model.RowsStaged</div>
    <div class="summary-card__label">
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
        Staged for Transfer Review
    </div>
</div>
```

Also add the conditional link after the existing `RowsFlagged` alert block. Find the line `@if (Model.RowsFlagged > 0)` and after its closing `}`, insert:

```cshtml
@if (Model.RowsStaged > 0)
{
    <div class="alert alert-warning mt-4">
        <strong>@Model.RowsStaged row(s) staged for transfer review.</strong>
        These rows look like inter-account transfers and need your confirmation before they appear as transactions.
        <a asp-controller="TransferReview" asp-action="Index">Review them now</a>.
    </div>
}
```

- [ ] **Step 3: Build**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/ImportController.cs \
        ProjectCeres/Views/Import/Summary.cshtml
git commit -m "feat: add RowsStaged to import summary — count card and link to Transfer Review"
```

---

## Task 10: Nav badge for pending staged transfer count

**Files:**
- Modify: `ProjectCeres/Views/Shared/_Layout.cshtml`

The layout injects `IRecurringTransactionService` for the upcoming-count badge. We follow the same pattern for pending staged transfers.

- [ ] **Step 1: Inject the count and pass to nav**

Open `ProjectCeres/Views/Shared/_Layout.cshtml`. It currently starts with:

```cshtml
@inject ProjectCeres.Services.IRecurringTransactionService RecurringTransactionService
@{
    var upcomingCount = (await RecurringTransactionService.GetUpcomingAsync(withinDays: 5)).Count();
}
```

Change to:

```cshtml
@inject ProjectCeres.Services.IRecurringTransactionService RecurringTransactionService
@inject ProjectCeres.Services.ITransferReviewService TransferReviewService
@{
    var upcomingCount      = (await RecurringTransactionService.GetUpcomingAsync(withinDays: 5)).Count();
    var pendingTransfers   = await TransferReviewService.GetPendingCountAsync();
}
```

Then find the `<div id="navbar-root"` line and change to:

```cshtml
<div id="navbar-root" data-upcoming-count="@upcomingCount" data-pending-transfers="@pendingTransfers"></div>
```

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres/ProjectCeres.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Shared/_Layout.cshtml
git commit -m "feat: pass pendingTransfers count to navbar via data attribute"
```

---

## Task 11: React nav badge update

The navbar is a React component rendered at `#navbar-root`. It already reads `data-upcoming-count`. We need it to also read `data-pending-transfers` and show a badge on a "Transfer Review" nav link when the count > 0.

**Files:**
- Modify: `ProjectCeres.Client/src/components/Navbar.tsx` (or wherever the nav component lives)

- [ ] **Step 1: Find the nav component**

```bash
find <repo>/ProjectCeres.Client/src -name "Navbar*" -o -name "navbar*" -o -name "Nav*" 2>/dev/null
```

Read whatever file is found to understand the current structure before editing.

- [ ] **Step 2: Read the component and understand the data-attribute pattern**

Read the component file to see how `data-upcoming-count` is consumed (via `dataset` on the root element, or via a prop).

- [ ] **Step 3: Add pending-transfers badge**

In the nav component, alongside wherever `upcomingCount` is read:

```tsx
const root = document.getElementById('navbar-root');
const pendingTransfers = parseInt(root?.dataset.pendingTransfers ?? '0', 10);
```

Add a nav link to `/TransferReview` that shows a badge when `pendingTransfers > 0`:

```tsx
<a href="/TransferReview" className="nav-link">
  Transfer Review
  {pendingTransfers > 0 && (
    <span className="nav-badge">{pendingTransfers}</span>
  )}
</a>
```

Use the same CSS class pattern as the existing upcoming-count badge. Do not invent new CSS — inspect the existing badge to find the class names used.

- [ ] **Step 4: Run the React build**

```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected: Build succeeded with no errors.

- [ ] **Step 5: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/
git commit -m "feat: show pending staged transfer count badge in nav"
```

---

## Task 12: Run full test suite and final verification

- [ ] **Step 1: Run all server tests**

```bash
cd <repo>
dotnet test ProjectCeres.Tests 2>&1 | tail -30
```

Expected: All tests pass, 0 failed.

- [ ] **Step 2: Run React tests**

```bash
cd <repo>/ProjectCeres.Client
pnpm test 2>&1 | tail -20
```

Expected: All tests pass, 0 failed.

- [ ] **Step 3: Manual smoke test — start the app**

```bash
cd <repo>
dotnet run --project ProjectCeres
```

Verify:
1. Navigate to `/Import` — form loads.
2. Import a CSV with two rows of opposite sign and same amount on the same date — summary shows "Staged for Transfer Review: 2".
3. Click the Transfer Review link — staged rows appear.
4. Resolve one row with "Not a Transfer" — it disappears from the list; a transaction appears in `/Transactions`.
5. Nav badge shows 1 (one remaining staged row).
6. Resolve the second row with "Create Transfer" and select an account — Transfer created, badge disappears.

- [ ] **Step 4: Final commit (if any cleanup needed)**

```bash
git status
# Stage any remaining changes and commit with descriptive message
```

---

## Self-Review: Spec Coverage Check

| Spec section | Covered by task |
|---|---|
| §1 Import Form — initial state (2 fields only) | Already done in Plan A; no change needed |
| §1 After upload — column mapping revealed | Already done in Plan A; no change needed |
| §1 Saved profiles | Already done in Plan A; no change needed |
| §1 Save profile offer on summary | Already done in Plan A; no change needed |
| §2 Column mapping dropdowns with auto-select | Already done in Plan A; no change needed |
| §3 Uncategorized Income/Expense fallback | Already done; `ImportService` uses these GUIDs |
| §4 Reconciliation — match before creating | Already done in existing `ImportService` |
| §4 Multiple candidates match — stage for manual confirmation | **Not explicitly wired** — the current reconciliation picks `FirstOrDefault`, so ambiguous matches always go to the first. This is a gap vs. the spec. See note below. |
| §5 Transfer detection pass 1 — intra-file pairing | Task 4–6 |
| §5 Transfer detection pass 1 — cross-account pairing | Task 4–6 |
| §5 Transfer detection pass 1 — keyword training check | Task 4–6 |
| §5 Pass 2 — Transfer Review screen | Task 7–8 |
| §5 Link to existing transaction | Task 7–8 |
| §5 Specify other account | Task 7–8 |
| §5 Not a transfer — dismiss + save pattern | Task 7–8 |
| §5 Training store `ImportTransferExclusion` | Task 1–2, 7 |
| §6 `ImportStagedTransfer` table | Task 1–2 |
| §7 Summary — Staged count card | Task 9 |
| §7 Summary — link to Transfer Review | Task 9 |
| §7 Save profile prompt | Already done in Plan A; no change needed |
| Nav indicator for pending staged count | Task 10–11 |

**Gap — reconciliation ambiguous match (§4):** The spec says "if multiple candidates match → stage for manual confirmation." The current code silently picks the first match with `FirstOrDefault`. This is a separate concern from transfer detection (it's about reconciliation ambiguity, not transfer pairing). Implementing it in this plan would require a new staged-reconciliation table and review flow, which the roadmap does not include in Stage 3.5 — it is implicit in the spec but not in the roadmap test matrix. **Leave this for a follow-up.** The existing behaviour (take first match) is safe and matches what existed before this feature.

**No placeholder issues found** — all steps contain actual code.

**Type consistency check:**
- `StagedTransferStatus.Pending` used in Task 1, 4, 5, 7 — consistent ✓
- `ImportStagedTransfer.RawAmount` is `decimal` — signed, as spec requires ✓
- `TransferDetectionResult` record defined in `ITransferDetectionService.cs`, consumed in `ImportService.cs` — consistent ✓
- `ITransferReviewService.GetPendingCountAsync()` defined in Task 7, called in `_Layout.cshtml` Task 10 — consistent ✓
