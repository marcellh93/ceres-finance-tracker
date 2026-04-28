# Reconciliation Staged Transaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stage every auto-reconciled CSV row as an `ImportStagedTransaction` so users can confirm or dispute fuzzy matches from a dedicated review screen.

**Architecture:** New `ImportStagedTransaction` model + EF migration. `ImportStagedTransactionService` handles confirm/dispute logic. `ReconciliationReviewController` serves the review screen. `ImportService.ImportAsync` gains one `.Add(...)` call per reconciled row. The Navbar React component gains a `pendingReconciliations` prop wired from `_Layout.cshtml`.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core + PostgreSQL, xUnit + FluentAssertions, React + TypeScript (Navbar prop only)

---

## File Map

### New files
| File | Purpose |
|---|---|
| `ProjectCeres/Models/ImportStagedTransaction.cs` | EF model |
| `ProjectCeres/Models/StagedTransactionStatus.cs` | Status enum |
| `ProjectCeres/Services/IImportStagedTransactionService.cs` | Interface |
| `ProjectCeres/Services/ImportStagedTransactionService.cs` | Implementation |
| `ProjectCeres/ViewModels/StagedTransactionViewModel.cs` | View model for review screen |
| `ProjectCeres/Controllers/ReconciliationReviewController.cs` | Controller |
| `ProjectCeres/Views/ReconciliationReview/Index.cshtml` | Review screen |
| `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs` | Integration tests |

### Modified files
| File | Change |
|---|---|
| `ProjectCeres/Data/AppDbContext.cs` | Add `DbSet<ImportStagedTransaction>` |
| `ProjectCeres/Program.cs` | Register `IImportStagedTransactionService` |
| `ProjectCeres/Services/ImportService.cs` | Stage reconciled rows |
| `ProjectCeres/Views/Import/Summary.cshtml` | Reconciled tile links to review screen |
| `ProjectCeres/Views/Shared/_Layout.cshtml` | Inject `pendingReconciliations` into navbar data attribute |
| `ProjectCeres.Client/src/components/ui/navbar.tsx` | Add `pendingReconciliations` prop + nav link |
| `ProjectCeres.Client/src/main.tsx` | Read `data-pending-reconciliations` attribute |

---

## Task 1: Model, Enum, Migration

**Files:**
- Create: `ProjectCeres/Models/StagedTransactionStatus.cs`
- Create: `ProjectCeres/Models/ImportStagedTransaction.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs`

- [ ] **Step 1: Create the status enum**

Create `ProjectCeres/Models/StagedTransactionStatus.cs`:

```csharp
namespace ProjectCeres.Models;

public enum StagedTransactionStatus
{
    Pending,
    Confirmed,
    Disputed
}
```

- [ ] **Step 2: Create the model**

Create `ProjectCeres/Models/ImportStagedTransaction.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class ImportStagedTransaction
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly RawDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid MatchedTransactionId { get; set; }
    public StagedTransactionStatus Status { get; set; } = StagedTransactionStatus.Pending;
    public DateTime? ResolvedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Transaction MatchedTransaction { get; set; } = null!;
}
```

- [ ] **Step 3: Register DbSet in AppDbContext**

In `ProjectCeres/Data/AppDbContext.cs`, add after the existing `ImportTransferExclusions` line:

```csharp
public DbSet<ImportStagedTransaction> ImportStagedTransactions => Set<ImportStagedTransaction>();
```

- [ ] **Step 4: Create and inspect the migration**

```bash
cd <repo>
dotnet ef migrations add AddImportStagedTransaction --project ProjectCeres
```

Open the generated migration file and verify:
- `Up()` creates a new `ImportStagedTransactions` table with all 9 columns
- `Down()` drops that table
- No `AlterTable`, `AddColumn`, or `DropColumn` on existing tables

- [ ] **Step 5: Apply migration and confirm build**

```bash
dotnet ef database update --project ProjectCeres
dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Models/StagedTransactionStatus.cs \
        ProjectCeres/Models/ImportStagedTransaction.cs \
        ProjectCeres/Data/AppDbContext.cs \
        ProjectCeres/Migrations/
git commit -m "feat(reconciliation): add ImportStagedTransaction model and migration"
```

---

## Task 2: Service Interface and Failing Tests

**Files:**
- Create: `ProjectCeres/Services/IImportStagedTransactionService.cs`
- Create: `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs`

- [ ] **Step 1: Create the interface**

Create `ProjectCeres/Services/IImportStagedTransactionService.cs`:

```csharp
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task ConfirmAsync(Guid id);
    Task ConfirmAllAsync();
    Task DisputeAsync(Guid id);
}
```

- [ ] **Step 2: Write all failing integration tests**

Create `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs`:

```csharp
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
        // one already confirmed — should not be touched
        var staged3 = await CreateStagedAsync(txId3, StagedTransactionStatus.Confirmed);

        await _service.ConfirmAllAsync();

        var r1 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged1.Id);
        var r2 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged2.Id);
        var r3 = await _fixture.Db.ImportStagedTransactions.FindAsync(staged3.Id);

        r1!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r2!.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r3!.ResolvedAt.Should().BeNull(); // was already confirmed, untouched
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
```

- [ ] **Step 3: Run tests — confirm they all fail because the service doesn't exist**

```bash
dotnet test ProjectCeres.Tests --filter "ImportStagedTransactionServiceTests" 2>&1 | tail -15
```

Expected: build error — `ImportStagedTransactionService` not found. That's the correct red state.

- [ ] **Step 4: Commit the failing tests and interface**

```bash
git add ProjectCeres/Services/IImportStagedTransactionService.cs \
        ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs
git commit -m "test(reconciliation): add failing integration tests for ImportStagedTransactionService"
```

---

## Task 3: Service Implementation

**Files:**
- Create: `ProjectCeres/Services/ImportStagedTransactionService.cs`
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Implement the service**

Create `ProjectCeres/Services/ImportStagedTransactionService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class ImportStagedTransactionService(
    AppDbContext db,
    ITransactionService transactionService) : IImportStagedTransactionService
{
    private static readonly Guid UncategorizedIncomeId  = new("20000000-0000-0000-0000-000000000025");
    private static readonly Guid UncategorizedExpenseId = new("20000000-0000-0000-0000-000000000026");

    public async Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync() =>
        await db.ImportStagedTransactions
            .Include(s => s.Account)
            .Include(s => s.MatchedTransaction)
            .Where(s => s.Status == StagedTransactionStatus.Pending)
            .OrderBy(s => s.ImportedAt)
            .ToListAsync();

    public async Task<int> GetPendingCountAsync() =>
        await db.ImportStagedTransactions
            .CountAsync(s => s.Status == StagedTransactionStatus.Pending);

    public async Task ConfirmAsync(Guid id)
    {
        var staged = await db.ImportStagedTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Staged transaction {id} not found.");

        staged.Status     = StagedTransactionStatus.Confirmed;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ConfirmAllAsync()
    {
        var pending = await db.ImportStagedTransactions
            .Where(s => s.Status == StagedTransactionStatus.Pending)
            .ToListAsync();

        foreach (var staged in pending)
        {
            staged.Status     = StagedTransactionStatus.Confirmed;
            staged.ResolvedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task DisputeAsync(Guid id)
    {
        var staged = await db.ImportStagedTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Staged transaction {id} not found.");

        await transactionService.MarkClearedAsync(staged.MatchedTransactionId, cleared: false);

        var categoryId = staged.RawAmount >= 0 ? UncategorizedIncomeId : UncategorizedExpenseId;
        var txId = await transactionService.CreateAsync(new TransactionCreateViewModel
        {
            Date        = staged.RawDate,
            Amount      = staged.RawAmount,
            Description = staged.RawDescription,
            AccountId   = staged.AccountId,
            CategoryId  = categoryId
        });
        await transactionService.MarkNeedsReviewAsync(txId, needsReview: true);

        staged.Status     = StagedTransactionStatus.Disputed;
        staged.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 2: Register the service in DI**

In `ProjectCeres/Program.cs`, add after the `ITransferReviewService` registration (around line 75):

```csharp
builder.Services.AddScoped<IImportStagedTransactionService, ImportStagedTransactionService>();
```

- [ ] **Step 3: Run the service tests — all must pass**

```bash
dotnet test ProjectCeres.Tests --filter "ImportStagedTransactionServiceTests" 2>&1 | tail -15
```

Expected: all tests pass.

- [ ] **Step 4: Run full suite — no regressions**

```bash
dotnet test ProjectCeres.Tests 2>&1 | tail -5
```

Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/ImportStagedTransactionService.cs \
        ProjectCeres/Program.cs
git commit -m "feat(reconciliation): implement ImportStagedTransactionService"
```

---

## Task 4: Wire ImportService to Stage Reconciled Rows

**Files:**
- Modify: `ProjectCeres/Services/ImportService.cs`
- Test: `ProjectCeres.Tests/Integration/ImportServiceTests.cs`

- [ ] **Step 1: Write the failing test first**

In `ProjectCeres.Tests/Integration/ImportServiceTests.cs`, add this test inside `ImportServiceIntegrationTests`:

```csharp
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
```

- [ ] **Step 2: Run test — confirm it fails**

```bash
dotnet test ProjectCeres.Tests --filter "ImportAsync_ReconciledRow_CreatesStagedTransactionRecord" 2>&1 | tail -10
```

Expected: FAIL — `staged` collection is empty (no staging happens yet).

- [ ] **Step 3: Update ImportService to accept and use the staged transaction service**

In `ProjectCeres/Services/ImportService.cs`, update the constructor and the reconciliation block:

Constructor — add `IImportStagedTransactionService? stagedTransactionService = null`:

```csharp
public ImportService(
    ImportParserFactory parserFactory,
    AppDbContext? db = null,
    ITransactionService? transactionService = null,
    ITransferDetectionService? transferDetectionService = null,
    IImportStagedTransactionService? stagedTransactionService = null) : IImportService
```

Then update the reconciliation block (around line 96, inside `if (match is not null)`):

```csharp
if (match is not null)
{
    if (!match.IsCleared)
    {
        await transactionService.MarkClearedAsync(match.Id, cleared: true);
    }

    if (stagedTransactionService is not null)
    {
        db.ImportStagedTransactions.Add(new ImportStagedTransaction
        {
            Id                   = Guid.NewGuid(),
            ImportedAt           = DateTime.UtcNow,
            AccountId            = accountId,
            RawDate              = row.Date,
            RawAmount            = Math.Abs(row.Amount),
            RawDescription       = row.Description,
            MatchedTransactionId = match.Id,
            Status               = StagedTransactionStatus.Pending
        });
        await db.SaveChangesAsync();
    }

    result.RowsReconciled++;
    continue;
}
```

- [ ] **Step 4: Wire the service into DI construction in Program.cs**

`ImportService` is registered as a scoped service. Find its registration in `ProjectCeres/Program.cs` and ensure `IImportStagedTransactionService` is injected. Because `ImportService` uses primary constructor DI, ASP.NET Core resolves it automatically — just confirm the registration exists:

```bash
grep -n "ImportService\|IImportService" ProjectCeres/Program.cs
```

If it's registered with `AddScoped<IImportService, ImportService>()`, ASP.NET Core will inject all constructor params automatically. No change needed beyond Task 3 Step 2.

- [ ] **Step 5: Run the new import test — must pass**

```bash
dotnet test ProjectCeres.Tests --filter "ImportAsync_ReconciledRow_CreatesStagedTransactionRecord" 2>&1 | tail -10
```

Expected: PASS.

- [ ] **Step 6: Run full suite — no regressions**

```bash
dotnet test ProjectCeres.Tests 2>&1 | tail -5
```

Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/ImportService.cs \
        ProjectCeres.Tests/Integration/ImportServiceTests.cs
git commit -m "feat(reconciliation): stage reconciled rows in ImportService"
```

---

## Task 5: View Model, Controller, View

**Files:**
- Create: `ProjectCeres/ViewModels/StagedTransactionViewModel.cs`
- Create: `ProjectCeres/Controllers/ReconciliationReviewController.cs`
- Create: `ProjectCeres/Views/ReconciliationReview/Index.cshtml`

- [ ] **Step 1: Create the view model**

Create `ProjectCeres/ViewModels/StagedTransactionViewModel.cs`:

```csharp
namespace ProjectCeres.ViewModels;

public class StagedTransactionViewModel
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly RawDate { get; set; }
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public string? MatchedTransactionDescription { get; set; }
    public DateOnly MatchedTransactionDate { get; set; }
    public decimal MatchedTransactionAmount { get; set; }
}
```

- [ ] **Step 2: Create the controller**

Create `ProjectCeres/Controllers/ReconciliationReviewController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers;

public class ReconciliationReviewController(
    IImportStagedTransactionService stagedTransactionService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var pending = await stagedTransactionService.GetPendingAsync();

        var vms = pending.Select(s => new StagedTransactionViewModel
        {
            Id                              = s.Id,
            ImportedAt                      = s.ImportedAt,
            AccountName                     = s.Account?.Name ?? s.AccountId.ToString(),
            RawDate                         = s.RawDate,
            RawAmount                       = s.RawAmount,
            RawDescription                  = s.RawDescription,
            MatchedTransactionDescription   = s.MatchedTransaction?.Description,
            MatchedTransactionDate          = s.MatchedTransaction?.Date ?? s.RawDate,
            MatchedTransactionAmount        = s.MatchedTransaction?.Amount ?? s.RawAmount
        }).ToList();

        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id)
    {
        try
        {
            await stagedTransactionService.ConfirmAsync(id);
            TempData["SuccessMessage"] = "Reconciliation confirmed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAll()
    {
        try
        {
            await stagedTransactionService.ConfirmAllAsync();
            TempData["SuccessMessage"] = "All reconciliations confirmed.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispute(Guid id)
    {
        try
        {
            await stagedTransactionService.DisputeAsync(id);
            TempData["SuccessMessage"] = "Match disputed — original transaction un-cleared and CSV row inserted as a new transaction.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
```

- [ ] **Step 3: Create the view directory and view**

```bash
mkdir -p <repo>/ProjectCeres/Views/ReconciliationReview
```

Create `ProjectCeres/Views/ReconciliationReview/Index.cshtml`:

```cshtml
@model IEnumerable<StagedTransactionViewModel>
@{ ViewData["Title"] = "Reconciliation Review"; }

<div class="page-header">
    <h1>Reconciliation Review</h1>
    <p class="text-sm text-gray-500">These rows were automatically matched to existing transactions during import. Confirm correct matches or dispute incorrect ones.</p>
</div>

@if (!Model.Any())
{
    <div class="card mt-4">
        <p class="text-gray-500 text-sm">No pending reconciliations.</p>
        <div class="mt-4">
            <a asp-controller="Transactions" asp-action="Index" class="btn btn-secondary">View Transactions</a>
        </div>
    </div>
}
else
{
    <div class="mt-4 mb-6">
        <form asp-action="ConfirmAll" method="post">
            @Html.AntiForgeryToken()
            <button type="submit" class="btn btn-secondary inline-flex items-center gap-1">
                <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                Confirm All
            </button>
        </form>
    </div>

    foreach (var row in Model)
    {
        <div class="card mt-4 p-4">
            <div class="flex items-start justify-between gap-4 flex-wrap">
                <div>
                    <div class="text-sm text-gray-500 mb-1">@row.AccountName — imported @row.ImportedAt.ToString("dd/MM/yyyy HH:mm")</div>
                    <div class="font-semibold text-gray-800">
                        @row.RawDate.ToString("dd/MM/yyyy") &nbsp;
                        <span class="@(row.RawAmount < 0 ? "text-red-600" : "text-green-700")">
                            @row.RawAmount.ToString("F2")
                        </span>
                    </div>
                    @if (!string.IsNullOrEmpty(row.RawDescription))
                    {
                        <div class="text-sm text-gray-600 mt-0.5">@row.RawDescription</div>
                    }

                    <div class="mt-2 text-sm text-blue-700 bg-blue-50 rounded px-2 py-1 inline-block">
                        Matched to: @row.MatchedTransactionDate.ToString("dd/MM/yyyy")
                        @row.MatchedTransactionAmount.ToString("F2")
                        @row.MatchedTransactionDescription
                    </div>
                </div>
            </div>

            <div class="mt-4 flex flex-wrap gap-3">
                <form asp-action="Confirm" method="post">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="id" value="@row.Id" />
                    <button type="submit" class="btn btn-primary text-sm inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                        Confirm Match
                    </button>
                </form>

                <form asp-action="Dispute" method="post">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="id" value="@row.Id" />
                    <button type="submit" class="btn btn-danger text-sm inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                        Not This Transaction
                    </button>
                </form>
            </div>
        </div>
    }
}
```

- [ ] **Step 4: Build to confirm no errors**

```bash
dotnet build ProjectCeres 2>&1 | tail -5
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/ViewModels/StagedTransactionViewModel.cs \
        ProjectCeres/Controllers/ReconciliationReviewController.cs \
        ProjectCeres/Views/ReconciliationReview/
git commit -m "feat(reconciliation): add ReconciliationReviewController and view"
```

---

## Task 6: Nav + Import Summary Wire-Up

**Files:**
- Modify: `ProjectCeres/Views/Shared/_Layout.cshtml`
- Modify: `ProjectCeres/Views/Import/Summary.cshtml`
- Modify: `ProjectCeres.Client/src/components/ui/navbar.tsx`
- Modify: `ProjectCeres.Client/src/main.tsx`

- [ ] **Step 1: Inject pending reconciliation count into _Layout.cshtml**

In `ProjectCeres/Views/Shared/_Layout.cshtml`, update the top `@inject` + data block (lines 1–6):

```cshtml
@inject ProjectCeres.Services.IRecurringTransactionService RecurringTransactionService
@inject ProjectCeres.Services.ITransferReviewService TransferReviewService
@inject ProjectCeres.Services.IImportStagedTransactionService ImportStagedTransactionService
@{
    var upcomingCount             = (await RecurringTransactionService.GetUpcomingAsync(withinDays: 5)).Count();
    var pendingTransfers          = await TransferReviewService.GetPendingCountAsync();
    var pendingReconciliations    = await ImportStagedTransactionService.GetPendingCountAsync();
}
```

Update the `navbar-root` div (line 17) to pass the new count:

```cshtml
<div id="navbar-root"
     data-upcoming-count="@upcomingCount"
     data-pending-transfers="@pendingTransfers"
     data-pending-reconciliations="@pendingReconciliations"></div>
```

- [ ] **Step 2: Update the Navbar React component**

In `ProjectCeres.Client/src/components/ui/navbar.tsx`, update the props interface and component:

```tsx
import { Bell } from 'lucide-react'
import { Badge } from '@/components/ui/badge'

interface NavbarProps {
  upcomingPaymentsCount?: number
  pendingTransfers?: number
  pendingReconciliations?: number
}

export function Navbar({ upcomingPaymentsCount = 0, pendingTransfers = 0, pendingReconciliations = 0 }: NavbarProps) {
  return (
    <nav className="bg-gray-900 text-white">
      <div className="mx-auto flex h-14 max-w-screen-xl items-center justify-between px-4">
        <a href="/Dashboard" className="font-bold tracking-tight !text-white hover:!text-gray-200 no-underline">Project Ceres</a>
        <div className="flex items-center gap-1 text-sm">
          <a href="/Dashboard" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Dashboard</a>
          <a href="/Movements" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Movements</a>
          <a href="/Accounts" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Accounts</a>
          <a href="/Transactions" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Transactions</a>
          <a href="/Transfers" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Transfers</a>
          <a href="/Categories" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Categories</a>
          <a href="/Budgets" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Budgets</a>
          <a href="/Import" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Import</a>
          <a href="/TransferReview" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            Transfer Review
            {pendingTransfers > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {pendingTransfers}
              </Badge>
            )}
          </a>
          <a href="/ReconciliationReview" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            Reconciliation
            {pendingReconciliations > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {pendingReconciliations}
              </Badge>
            )}
          </a>
          <a href="/RecurringTransactions/Upcoming" className="relative !text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline inline-flex items-center gap-1.5">
            <Bell className="h-4 w-4" />
            Reminders
            {upcomingPaymentsCount > 0 && (
              <Badge className="ml-1.5" variant="destructive">
                {upcomingPaymentsCount}
              </Badge>
            )}
          </a>
          <a href="/Reports" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Reports</a>
          <a href="/Settings/Edit" className="!text-gray-300 hover:!text-white hover:bg-gray-800 px-3 py-1.5 rounded-md transition-colors no-underline">Settings</a>
        </div>
      </div>
    </nav>
  )
}
```

- [ ] **Step 3: Update main.tsx to read the new data attribute**

In `ProjectCeres.Client/src/main.tsx`, update the navbar mount block (lines 16–25):

```tsx
const navbarEl = document.getElementById('navbar-root')
if (navbarEl) {
  const upcomingCount           = parseInt(navbarEl.dataset.upcomingCount ?? '0', 10)
  const pendingTransfers        = parseInt(navbarEl.dataset.pendingTransfers ?? '0', 10)
  const pendingReconciliations  = parseInt(navbarEl.dataset.pendingReconciliations ?? '0', 10)
  createRoot(navbarEl).render(
    <StrictMode>
      <Navbar
        upcomingPaymentsCount={upcomingCount}
        pendingTransfers={pendingTransfers}
        pendingReconciliations={pendingReconciliations}
      />
    </StrictMode>,
  )
}
```

- [ ] **Step 4: Update Import Summary — Reconciled tile becomes a link**

In `ProjectCeres/Views/Import/Summary.cshtml`, replace the existing Reconciled tile:

```cshtml
<div class="card summary-card summary-card--info">
    <div class="summary-card__value">
        @if (Model.RowsReconciled > 0)
        {
            <a asp-controller="ReconciliationReview" asp-action="Index" class="hover:underline">@Model.RowsReconciled</a>
        }
        else
        {
            @Model.RowsReconciled
        }
    </div>
    <div class="summary-card__label">
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
        Reconciled
    </div>
</div>
```

- [ ] **Step 5: Build both projects**

```bash
dotnet build ProjectCeres 2>&1 | tail -5
cd ProjectCeres.Client && pnpm build 2>&1 | tail -10
```

Expected: 0 errors on both.

- [ ] **Step 6: Run full test suite**

```bash
cd <repo>
dotnet test ProjectCeres.Tests 2>&1 | tail -5
```

Expected: 0 failed.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Views/Shared/_Layout.cshtml \
        ProjectCeres/Views/Import/Summary.cshtml \
        ProjectCeres.Client/src/components/ui/navbar.tsx \
        ProjectCeres.Client/src/main.tsx
git commit -m "feat(reconciliation): wire nav indicator and summary tile link"
```

---

## Task 7: Update Roadmap Checklist

**Files:**
- Modify: `docs/roadmap-phase-two.md`

- [ ] **Step 1: Mark the checklist item**

In `docs/roadmap-phase-two.md`, find and update:

```markdown
- [ ] "Different transaction" reconciliation: user marks the flagged row as a new distinct transaction → row cleared automatically; no manual follow-up required _(not implemented)_
```

Replace with:

```markdown
- [x] "Different transaction" reconciliation: user marks the flagged row as a new distinct transaction → row un-cleared, CSV row inserted as new transaction with `NeedsReview = true` — via `ReconciliationReview` screen (`DisputeAsync` in `ImportStagedTransactionService`)
```

- [ ] **Step 2: Final full test run**

```bash
dotnet test ProjectCeres.Tests 2>&1 | tail -5
cd ProjectCeres.Client && pnpm test 2>&1 | tail -5
```

Expected: 0 failed on both.

- [ ] **Step 3: Commit**

```bash
git add docs/roadmap-phase-two.md
git commit -m "docs: mark reconciliation dispute checklist item complete"
```
