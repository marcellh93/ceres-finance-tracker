# Stage 10 — CSV Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a filter-aware "Export CSV" button to the Transactions Index page that downloads the current view as a `.csv` file with CSV injection prevention.

**Architecture:** Extract shared CSV formatting helpers from `ReportsController` into a static `CsvFormattingHelper` class. Add `ITransactionExportService` / `TransactionExportService` (queries filtered `Transaction` entities, no pagination). Add `TransactionsController.Export` action and an Export button on the Index view.

**Tech Stack:** ASP.NET Core MVC, Entity Framework Core (Npgsql), xUnit + FluentAssertions (server tests), Razor `.cshtml` views.

---

## File Map

| Status | File | Responsibility |
|---|---|---|
| **Create** | `ProjectCeres/Helpers/CsvFormattingHelper.cs` | Static helpers: `Csv(string?)` sanitisation, `CsvFile(List<string>, string)` builder |
| **Create** | `ProjectCeres/Services/ITransactionExportService.cs` | Interface: `ExportAsync(accountId?, from?, to?)` |
| **Create** | `ProjectCeres/Services/TransactionExportService.cs` | Implementation: EF query → `IReadOnlyList<TransactionExportRow>` |
| **Create** | `ProjectCeres.Tests/Unit/CsvFormattingHelperTests.cs` | Unit tests for CSV injection rules |
| **Create** | `ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs` | Integration tests for export filter logic |
| **Modify** | `ProjectCeres/Controllers/ReportsController.cs` | Replace private `Csv()` / `CsvFile()` with `CsvFormattingHelper` calls |
| **Modify** | `ProjectCeres/Controllers/TransactionsController.cs` | Add `Export` action; inject `ITransactionExportService` |
| **Modify** | `ProjectCeres/Views/Transactions/Index.cshtml` | Add Export CSV button with inline SVG download icon |
| **Modify** | `ProjectCeres/Program.cs` | Register `ITransactionExportService` → `TransactionExportService` |

---

## Task 1: Extract `CsvFormattingHelper` (shared static class)

**Files:**
- Create: `ProjectCeres/Helpers/CsvFormattingHelper.cs`
- Modify: `ProjectCeres/Controllers/ReportsController.cs`

- [ ] **Step 1: Write the failing unit tests**

  Create `ProjectCeres.Tests/Unit/CsvFormattingHelperTests.cs`:

  ```csharp
  using FluentAssertions;
  using ProjectCeres.Helpers;

  namespace ProjectCeres.Tests.Unit;

  public class CsvFormattingHelperTests
  {
      [Theory]
      [InlineData("=SUM(A1)", "'=SUM(A1)")]
      [InlineData("@user",    "'@user")]
      [InlineData("+1234",    "'+1234")]
      [InlineData("-1234",    "'-1234")]
      public void Csv_DangerousPrefix_IsPrefixedWithSingleQuote(string input, string expected)
          => CsvFormattingHelper.Csv(input).Should().Be(expected);

      [Fact]
      public void Csv_NormalValue_ReturnedUnchanged()
          => CsvFormattingHelper.Csv("Hello world").Should().Be("Hello world");

      [Fact]
      public void Csv_ValueContainingComma_WrappedInDoubleQuotes()
          => CsvFormattingHelper.Csv("Rent, utilities").Should().Be("\"Rent, utilities\"");

      [Fact]
      public void Csv_ValueContainingDoubleQuote_EscapedAndWrapped()
          => CsvFormattingHelper.Csv("Say \"hi\"").Should().Be("\"Say \"\"hi\"\"\"");

      [Fact]
      public void Csv_NullValue_ReturnsEmptyString()
          => CsvFormattingHelper.Csv(null).Should().Be("");

      [Fact]
      public void Csv_EmptyString_ReturnsEmptyString()
          => CsvFormattingHelper.Csv("").Should().Be("");
  }
  ```

- [ ] **Step 2: Run the tests — confirm they all fail**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~CsvFormattingHelperTests" --no-build 2>&1 | tail -10
  ```

  Expected: compile error — `ProjectCeres.Helpers.CsvFormattingHelper` does not exist.

- [ ] **Step 3: Create `CsvFormattingHelper`**

  Create `ProjectCeres/Helpers/CsvFormattingHelper.cs`:

  ```csharp
  using Microsoft.AspNetCore.Mvc;

  namespace ProjectCeres.Helpers;

  public static class CsvFormattingHelper
  {
      public static string Csv(string? value)
      {
          if (string.IsNullOrEmpty(value)) return "";
          if (value[0] is '=' or '@' or '+' or '-') value = "'" + value;
          return value.Contains(',') || value.Contains('"') || value.Contains('\n')
              ? $"\"{value.Replace("\"", "\"\"")}\""
              : value;
      }

      public static FileContentResult CsvFile(ControllerBase controller, List<string> lines, string fileName)
      {
          var content = string.Join("\n", lines);
          var bytes   = System.Text.Encoding.UTF8.GetPreamble()
              .Concat(System.Text.Encoding.UTF8.GetBytes(content))
              .ToArray();
          return controller.File(bytes, "text/csv; charset=utf-8", fileName);
      }
  }
  ```

  > Note: `CsvFile` takes a `ControllerBase` parameter because `File(...)` is an instance method on the controller. The calling controller passes `this`.

- [ ] **Step 4: Run unit tests — confirm they pass**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~CsvFormattingHelperTests" 2>&1 | tail -10
  ```

  Expected: `6 passed, 0 failed`.

- [ ] **Step 5: Update `ReportsController` to use `CsvFormattingHelper`**

  Open `ProjectCeres/Controllers/ReportsController.cs`. Replace the two private methods at the bottom of the class:

  ```csharp
  // REMOVE these two private methods:
  private static string Csv(string? value) { ... }
  private FileContentResult CsvFile(List<string> lines, string fileName) { ... }
  ```

  Add this using at the top of the file (alongside the other usings):

  ```csharp
  using ProjectCeres.Helpers;
  ```

  Replace every call to the private `Csv(...)` with `CsvFormattingHelper.Csv(...)`.
  Replace every call to `CsvFile(lines, fileName)` with `CsvFormattingHelper.CsvFile(this, lines, fileName)`.

  There are 8 calls to `Csv(...)` and 8 calls to `CsvFile(...)` in the file. Use search-and-replace within the file — the private `Csv` and `CsvFile` only appear inside `ReportsController`.

- [ ] **Step 6: Build to confirm no errors**

  ```bash
  dotnet build ProjectCeres 2>&1 | grep -E "error|warning" | head -20
  ```

  Expected: zero errors, zero warnings.

- [ ] **Step 7: Run the full test suite — confirm nothing broke**

  ```bash
  dotnet test ProjectCeres.Tests 2>&1 | tail -5
  ```

  Expected: all existing tests pass.

- [ ] **Step 8: Commit**

  ```bash
  git add ProjectCeres/Helpers/CsvFormattingHelper.cs \
          ProjectCeres/Controllers/ReportsController.cs \
          ProjectCeres.Tests/Unit/CsvFormattingHelperTests.cs
  git commit -m "refactor: extract CsvFormattingHelper; add unit tests"
  ```

---

## Task 2: `ITransactionExportService` + `TransactionExportService`

**Files:**
- Create: `ProjectCeres/Services/ITransactionExportService.cs`
- Create: `ProjectCeres/Services/TransactionExportService.cs`
- Create: `ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs`
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Write the failing integration tests**

  Create `ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs`:

  ```csharp
  using FluentAssertions;
  using Moq;
  using ProjectCeres.Models;
  using ProjectCeres.Services;

  namespace ProjectCeres.Tests.Integration;

  /// <summary>
  /// Integration tests for TransactionExportService against the real project_ceres_test database.
  /// Each test rolls back — no data persists.
  ///
  /// Seed data used:
  ///   AccountTypeId 1 = Asset, CurrencyId 1 = EUR
  ///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary (Income, non-system)
  ///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
  /// </summary>
  [Collection("IntegrationTests")]
  public class TransactionExportServiceTests : IAsyncLifetime
  {
      private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
      private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

      private readonly TestDbFixture    _fixture = new();
      private TransactionExportService  _service = null!;
      private AccountService            _accountService = null!;
      private Guid                      _accountId;

      public async Task InitializeAsync()
      {
          await _fixture.InitAsync();
          _accountService = new AccountService(_fixture.Db);
          _service = new TransactionExportService(_fixture.Db);

          var account = new Account
          {
              Id            = Guid.NewGuid(),
              Name          = $"Export Test {Guid.NewGuid():N}",
              AccountTypeId = 1,
              CurrencyId    = 1,
              IsActive      = true
          };
          _fixture.Db.Accounts.Add(account);
          await _fixture.Db.SaveChangesAsync();
          _accountId = account.Id;
      }

      public async Task DisposeAsync() => await _fixture.DisposeAsync();

      private Transaction MakeTx(DateOnly date, decimal amount = 50m, string? desc = null) => new()
      {
          Id         = Guid.NewGuid(),
          AccountId  = _accountId,
          CategoryId = HousingCategoryId,
          Date       = date,
          Amount     = amount,
          Description = desc,
          CreatedAt  = DateTime.UtcNow
      };

      [Fact]
      public async Task ExportAsync_NoFilter_ReturnsAllTransactions()
      {
          var today = DateOnly.FromDateTime(DateTime.Today);
          _fixture.Db.Transactions.AddRange(
              MakeTx(today, 10m),
              MakeTx(today, 20m),
              MakeTx(today, 30m),
              MakeTx(today, 40m),
              MakeTx(today, 50m));
          await _fixture.Db.SaveChangesAsync();

          var rows = await _service.ExportAsync();

          rows.Count.Should().Be(5);
      }

      [Fact]
      public async Task ExportAsync_AccountIdFilter_ReturnsOnlyMatchingAccount()
      {
          var today = DateOnly.FromDateTime(DateTime.Today);

          // Second account
          var other = new Account
          {
              Id = Guid.NewGuid(), Name = $"Other {Guid.NewGuid():N}",
              AccountTypeId = 1, CurrencyId = 1, IsActive = true
          };
          _fixture.Db.Accounts.Add(other);
          await _fixture.Db.SaveChangesAsync();

          _fixture.Db.Transactions.AddRange(
              MakeTx(today, 10m),  // _accountId
              MakeTx(today, 20m)); // _accountId
          _fixture.Db.Transactions.Add(new Transaction
          {
              Id = Guid.NewGuid(), AccountId = other.Id,
              CategoryId = HousingCategoryId, Date = today,
              Amount = 99m, CreatedAt = DateTime.UtcNow
          });
          await _fixture.Db.SaveChangesAsync();

          var rows = await _service.ExportAsync(accountId: _accountId);

          rows.Should().HaveCount(2);
          rows.Should().OnlyContain(r => r.Account != null);
      }

      [Fact]
      public async Task ExportAsync_DateRangeFilter_ReturnsOnlyInRangeRows()
      {
          var jan1  = new DateOnly(2025, 1, 1);
          var jan15 = new DateOnly(2025, 1, 15);
          var feb1  = new DateOnly(2025, 2, 1);

          _fixture.Db.Transactions.AddRange(
              MakeTx(jan1,  10m),   // inside
              MakeTx(jan15, 20m),   // inside
              MakeTx(feb1,  30m));  // outside
          await _fixture.Db.SaveChangesAsync();

          var rows = await _service.ExportAsync(
              from: new DateOnly(2025, 1, 1),
              to:   new DateOnly(2025, 1, 31));

          rows.Should().HaveCount(2);
          rows.Should().OnlyContain(r => r.Date <= new DateOnly(2025, 1, 31));
      }

      [Fact]
      public async Task ExportAsync_NoMatchingTransactions_ReturnsEmptyList()
      {
          var rows = await _service.ExportAsync(
              from: new DateOnly(1900, 1, 1),
              to:   new DateOnly(1900, 12, 31));

          rows.Should().BeEmpty();
      }
  }
  ```

- [ ] **Step 2: Run to confirm compile failure**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionExportServiceTests" --no-build 2>&1 | tail -10
  ```

  Expected: compile error — `TransactionExportService` does not exist.

- [ ] **Step 3: Create the interface**

  Create `ProjectCeres/Services/ITransactionExportService.cs`:

  ```csharp
  namespace ProjectCeres.Services;

  public record TransactionExportRow(
      DateOnly Date,
      string   Account,
      string   Category,
      string   CategoryType,
      string?  Description,
      decimal  Amount);

  public interface ITransactionExportService
  {
      Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
          Guid?    accountId = null,
          DateOnly? from     = null,
          DateOnly? to       = null);
  }
  ```

- [ ] **Step 4: Create the implementation**

  Create `ProjectCeres/Services/TransactionExportService.cs`:

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using ProjectCeres.Data;

  namespace ProjectCeres.Services;

  public class TransactionExportService(AppDbContext db) : ITransactionExportService
  {
      public async Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
          Guid?     accountId = null,
          DateOnly? from      = null,
          DateOnly? to        = null)
      {
          var query = db.Transactions
              .Where(t => !t.Category.IsSystem)
              .Include(t => t.Account)
              .Include(t => t.Category).ThenInclude(c => c.CategoryType)
              .AsQueryable();

          if (accountId.HasValue)
              query = query.Where(t => t.AccountId == accountId.Value);
          if (from.HasValue)
              query = query.Where(t => t.Date >= from.Value);
          if (to.HasValue)
              query = query.Where(t => t.Date <= to.Value);

          var transactions = await query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToListAsync();

          return transactions.Select(t => new TransactionExportRow(
              t.Date,
              t.Account.Name,
              t.Category.Name,
              t.Category.CategoryType.Name,
              t.Description,
              t.Amount)).ToList();
      }
  }
  ```

- [ ] **Step 5: Register in DI**

  Open `ProjectCeres/Program.cs`. After the line:

  ```csharp
  builder.Services.AddScoped<ITransactionService, TransactionService>();
  ```

  Add:

  ```csharp
  builder.Services.AddScoped<ITransactionExportService, TransactionExportService>();
  ```

- [ ] **Step 6: Run integration tests — confirm they pass**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionExportServiceTests" 2>&1 | tail -10
  ```

  Expected: `4 passed, 0 failed`.

- [ ] **Step 7: Run the full test suite**

  ```bash
  dotnet test ProjectCeres.Tests 2>&1 | tail -5
  ```

  Expected: all tests pass.

- [ ] **Step 8: Commit**

  ```bash
  git add ProjectCeres/Services/ITransactionExportService.cs \
          ProjectCeres/Services/TransactionExportService.cs \
          ProjectCeres/Program.cs \
          ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs
  git commit -m "feat: add TransactionExportService with filter support"
  ```

---

## Task 3: `TransactionsController.Export` action + WAF test

**Files:**
- Modify: `ProjectCeres/Controllers/TransactionsController.cs`
- Create: (WAF test appended to existing `MovementsControllerTests.cs` or a new file)

- [ ] **Step 1: Write the failing WAF test**

  Create `ProjectCeres.Tests/Integration/TransactionExportControllerTests.cs`:

  ```csharp
  using System.Net;
  using FluentAssertions;
  using Microsoft.AspNetCore.Mvc.Testing;

  namespace ProjectCeres.Tests.Integration;

  [Collection("IntegrationTests")]
  public class TransactionExportControllerTests(TestWebApplicationFactory factory)
      : IClassFixture<TestWebApplicationFactory>
  {
      private readonly HttpClient _client = factory.CreateClient();

      [Fact]
      public async Task GetExport_NoFilter_Returns200WithCsvContentType()
      {
          var response = await _client.GetAsync("/Transactions/Export");

          response.StatusCode.Should().Be(HttpStatusCode.OK);
          response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
      }

      [Fact]
      public async Task GetExport_WithDateRange_Returns200WithCsvContentType()
      {
          var response = await _client.GetAsync("/Transactions/Export?from=2025-01-01&to=2025-12-31");

          response.StatusCode.Should().Be(HttpStatusCode.OK);
          response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
      }
  }
  ```

- [ ] **Step 2: Run to confirm failure**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionExportControllerTests" --no-build 2>&1 | tail -10
  ```

  Expected: compile error or 404 — `Export` action does not exist.

- [ ] **Step 3: Add `Export` action to `TransactionsController`**

  Open `ProjectCeres/Controllers/TransactionsController.cs`.

  Add `ITransactionExportService` to the primary constructor. The current constructor is:

  ```csharp
  public TransactionsController(ITransactionService transactionService, IFileAttachmentService attachmentService, AppDbContext db) : Controller
  ```

  Change it to:

  ```csharp
  public TransactionsController(
      ITransactionService transactionService,
      ITransactionExportService exportService,
      IFileAttachmentService attachmentService,
      AppDbContext db) : Controller
  ```

  Add the using at the top of the file (alongside existing usings):

  ```csharp
  using ProjectCeres.Helpers;
  ```

  Add the `Export` action after the `Index` action:

  ```csharp
  public async Task<IActionResult> Export(Guid? accountId, DateOnly? from, DateOnly? to)
  {
      var rows = await exportService.ExportAsync(accountId, from, to);

      var lines = new List<string> { "Date,Account,Category,Type,Description,Amount" };
      foreach (var r in rows)
          lines.Add($"{r.Date:yyyy-MM-dd},{CsvFormattingHelper.Csv(r.Account)},{CsvFormattingHelper.Csv(r.Category)},{CsvFormattingHelper.Csv(r.CategoryType)},{CsvFormattingHelper.Csv(r.Description)},{r.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");

      var fileName = from.HasValue && to.HasValue
          ? $"transactions_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.csv"
          : $"transactions_{DateTime.Today:yyyy-MM-dd}.csv";

      return CsvFormattingHelper.CsvFile(this, lines, fileName);
  }
  ```

- [ ] **Step 4: Run WAF tests — confirm they pass**

  ```bash
  dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionExportControllerTests" 2>&1 | tail -10
  ```

  Expected: `2 passed, 0 failed`.

- [ ] **Step 5: Run the full test suite**

  ```bash
  dotnet test ProjectCeres.Tests 2>&1 | tail -5
  ```

  Expected: all tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add ProjectCeres/Controllers/TransactionsController.cs \
          ProjectCeres.Tests/Integration/TransactionExportControllerTests.cs
  git commit -m "feat: add Export action to TransactionsController"
  ```

---

## Task 4: Export CSV button on Transactions Index

**Files:**
- Modify: `ProjectCeres/Views/Transactions/Index.cshtml`

- [ ] **Step 1: Add the Export CSV button**

  Open `ProjectCeres/Views/Transactions/Index.cshtml`. Find the `<div class="page-header">` block at the top (lines 4–10):

  ```html
  <div class="page-header">
      <h1>Transactions</h1>
      <a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          New Transaction
      </a>
  </div>
  ```

  Replace it with:

  ```html
  <div class="page-header">
      <h1>Transactions</h1>
      <div class="flex items-center gap-2">
          <a href="@Url.Action("Export", new { accountId = ViewBag.AccountId, from = ViewBag.From, to = ViewBag.To })"
             class="btn btn-secondary inline-flex items-center gap-1.5">
              <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
              Export CSV
          </a>
          <a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
              <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
              New Transaction
          </a>
      </div>
  </div>
  ```

  The `Url.Action("Export", new { accountId = ViewBag.AccountId, from = ViewBag.From, to = ViewBag.To })` passes the current filter state so the export always mirrors the active view.

- [ ] **Step 2: Build and verify no errors**

  ```bash
  dotnet build ProjectCeres 2>&1 | grep -E "error|warning" | head -20
  ```

  Expected: zero errors, zero warnings.

- [ ] **Step 3: Run the full test suite**

  ```bash
  dotnet test ProjectCeres.Tests 2>&1 | tail -5
  ```

  Expected: all tests pass.

- [ ] **Step 4: Update the roadmap checklist**

  Open `docs/roadmap-phase-two.md`. Find the CSV Export section (around line 885) and mark the three automated items as done:

  ```markdown
  - [x] Export all transactions (no filter) → CSV row count matches transaction count in the database
  - [x] Export with date range filter → CSV contains only transactions within that range
  - [x] CSV injection prevention: transaction with description starting with `=` → exported cell prefixed with `'`; opens correctly in Excel/Google Sheets without formula execution
  ```

- [ ] **Step 5: Commit**

  ```bash
  git add ProjectCeres/Views/Transactions/Index.cshtml \
          docs/roadmap-phase-two.md
  git commit -m "feat: add Export CSV button to Transactions Index; update roadmap checklist"
  ```
