# Movements + Quick-Add Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the Movements list page to the SPA at `/app/movements` with text search + 3 existing filters and an inline cleared toggle, wire the dashboard's `+` button to a real quick-add modal that creates transactions / transfers / liability payments without leaving the current page, and adopt Sonner as the SPA-wide toast system.

**Architecture:** Extend `IMovementService` with a `q` text-search parameter (matches description OR category name). Add `GET /api/movements` returning `MovementsPageDto`. Add three POST create endpoints — `POST /api/transactions`, `POST /api/transfers`, `POST /api/liability-payments` — each in its own thin API controller. Add two combobox helper endpoints — `GET /api/accounts/active`, `GET /api/categories/active`. Extend the existing `PATCH /api/movements/{id}/cleared` endpoint to support liability payments. Frontend renders Movements at `/app/movements` with URL-encoded filter state (`useApi`'s `AbortController` handles concurrency). Quick-add lives in a single `<QuickAddModal>` mounted from both the TopBar `+` button and a Movements page header "+ New" button. Sonner replaces the existing placeholder toasts.

**Tech Stack:** ASP.NET Core MVC + EF Core + xUnit/FluentAssertions on the server; React 19 + TypeScript + Vite + React Router + Tailwind v4 + shadcn/ui (`base-nova`) + Sonner + Vitest/RTL on the client.

**Spec:** `docs/superpowers/specs/2026-04-30-movements-and-quickadd-design.md`

---

## File Map

**Backend — create:**
- `ProjectCeres/ViewModels/MovementsApiDtos.cs` — wrapper DTO + list item DTO (named `*ApiDtos.cs` to avoid clash with existing `MovementListItemViewModel.cs`)
- `ProjectCeres/ViewModels/QuickAddDtos.cs` — three create-request DTOs (`CreateTransactionRequest`, `CreateTransferRequest`, `CreateLiabilityPaymentRequest`)
- `ProjectCeres/ViewModels/ComboboxOptionDtos.cs` — `AccountOptionDto`, `CategoryOptionDto`
- `ProjectCeres/Controllers/Api/TransactionsApiController.cs`
- `ProjectCeres/Controllers/Api/TransfersApiController.cs`
- `ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs`
- `ProjectCeres/Controllers/Api/AccountsApiController.cs`
- `ProjectCeres/Controllers/Api/CategoriesApiController.cs`

**Backend — modify:**
- `ProjectCeres/Services/IMovementService.cs` — add `string? q` to both methods
- `ProjectCeres/Services/MovementService.cs` — implement `q` filter in transaction/transfer/liability-payment query helpers
- `ProjectCeres/Controllers/Api/MovementsApiController.cs` — add `GetMovements()` GET; extend PATCH switch to handle `"liabilitypayment"`
- `ProjectCeres/Services/ILiabilityPaymentService.cs` + `LiabilityPaymentService.cs` — add `MarkClearedAsync(Guid id, bool cleared)`
- `ProjectCeres/Controllers/MovementsController.cs` — replace `Index()` body with `Redirect("/app/movements")`
- `ProjectCeres.Tests/Integration/MovementServiceTests.cs` — extend tests for `q` parameter
- `ProjectCeres.Tests/Integration/MovementsApiTests.cs` — extend tests for the new GET; extend PATCH tests for liability payments
- `ProjectCeres.Tests/Integration/MovementsControllerTests.cs` — replace 200-OK assertion with 302-to-`/app/movements` assertion

**Backend — delete:**
- `ProjectCeres/Views/Movements/Index.cshtml` (after redirect proven)

**Frontend — create:**
- `ProjectCeres.Client/src/app/features/movements/movements-api.ts`
- `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx` + `.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx` + `.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementsPagination.tsx` + `.test.tsx`
- `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx` + `.test.tsx` — SPA version that PATCHes the API directly (the existing `src/components/IsClearedSwitch.tsx` is a Razor form input — different contract, can't reuse)
- `ProjectCeres.Client/src/app/components/QuickAddModal.tsx` + `.test.tsx`
- `ProjectCeres.Client/src/app/components/AccountCombobox.tsx` + `.test.tsx` — searchable account selector (used 4× in QuickAddModal)
- `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx` + `.test.tsx` — searchable category selector (used in Transaction tab)
- `ProjectCeres.Client/src/app/lib/use-debounced.ts` + `.test.ts` — small debounce hook for the search input
- `ProjectCeres.Client/src/components/ui/sonner.tsx` (added by `pnpm dlx shadcn add sonner`)

**Frontend — modify:**
- `ProjectCeres.Client/src/app/pages/Movements.tsx` — replace placeholder with the real page
- `ProjectCeres.Client/src/app/layout/AppLayout.tsx` — mount `<Toaster />`
- `ProjectCeres.Client/src/app/layout/TopBar.tsx` — replace placeholder dialog with `<QuickAddModal>`

**Docs — modify:**
- `docs/api-contract.md` — add 6 new endpoint rows
- `docs/planning-phase3-spa-migration.md` — mark MovementsController as migrated; note partial Transactions/Transfers/LiabilityPayments API
- `docs/planning-phase3.md` — record progress under item 6
- `docs/design-system.md` — add Sonner toast usage notes

---

## Task Order Rationale

Backend first so the frontend can consume real shapes (Tasks 1–8). Then frontend infrastructure (Sonner + utilities) (Tasks 9–11). Then comboboxes and the quick-add modal in isolation (Tasks 12–14). Then the Movements page pieces (Tasks 15–18). Then layout integration (Task 19). Then Razor cleanup (Task 20). Finally docs (Task 21).

---

### Task 1: Add `q` parameter to `IMovementService` (interface + tests)

**Files:**
- Modify: `ProjectCeres/Services/IMovementService.cs`
- Modify: `ProjectCeres.Tests/Integration/MovementServiceTests.cs` — add new `q` filter tests

- [ ] **Step 1: Write the failing test**

Append to `ProjectCeres.Tests/Integration/MovementServiceTests.cs` (inside the existing class — the file already has setup/teardown for seeded transactions/transfers/payments):

```csharp
[Fact]
public async Task GetRecent_FiltersByDescriptionOrCategoryName_WhenQProvided()
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var svc = scope.ServiceProvider.GetRequiredService<IMovementService>();

    var account = SeedAssetAccount(db, "Checking");
    SeedTransaction(db, account.Id, SalaryCategoryId, amount: 100, description: "March salary");
    SeedTransaction(db, account.Id, HousingCategoryId, amount: 800, description: "Rent payment");
    SeedTransaction(db, account.Id, SalaryCategoryId, amount: 200, description: "Bonus");
    await db.SaveChangesAsync();

    // Match by description
    var rentResults = await svc.GetRecentAsync(q: "rent");
    rentResults.Should().HaveCount(1);
    rentResults[0].Description.Should().Be("Rent payment");

    // Match by category name (Salary category)
    var salaryResults = await svc.GetRecentAsync(q: "salary");
    salaryResults.Should().HaveCount(2);

    // Empty q returns all
    var allResults = await svc.GetRecentAsync(q: "");
    allResults.Should().HaveCount(3);

    // Null q returns all
    var nullResults = await svc.GetRecentAsync(q: null);
    nullResults.Should().HaveCount(3);
}

[Fact]
public async Task Count_RespectsQFilter()
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var svc = scope.ServiceProvider.GetRequiredService<IMovementService>();

    var account = SeedAssetAccount(db, "Checking");
    SeedTransaction(db, account.Id, SalaryCategoryId, amount: 100, description: "March salary");
    SeedTransaction(db, account.Id, HousingCategoryId, amount: 800, description: "Rent payment");
    await db.SaveChangesAsync();

    (await svc.CountAsync(q: "rent")).Should().Be(1);
    (await svc.CountAsync(q: null)).Should().Be(2);
}
```

(The seed helpers `SeedAssetAccount` and `SeedTransaction` already exist in this test file's helper section — reuse them. If a helper signature needs adjusting, do it inline.)

- [ ] **Step 2: Run the test to verify it fails (compile error)**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetRecent_FiltersByDescriptionOrCategoryName"`
Expected: FAIL — compile error, `IMovementService.GetRecentAsync` has no `q` parameter.

- [ ] **Step 3: Add `q` to the interface**

Replace the contents of `ProjectCeres/Services/IMovementService.cs` with:

```csharp
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IMovementService
{
    Task<List<MovementListItemViewModel>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0,
        string? q = null);

    Task<int> CountAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? q = null);
}
```

- [ ] **Step 4: Implement `q` in `MovementService`**

In `ProjectCeres/Services/MovementService.cs`:

a) Update both public method signatures to add `string? q = null`, and pass `q` to all three `Query*` helpers.

b) Update each of `QueryTransactions`, `QueryTransfers`, `QueryLiabilityPayments` to accept `string? q` and apply the filter when `q` is non-empty:

```csharp
private async Task<List<MovementListItemViewModel>> QueryTransactions(
    Guid? accountId, DateOnly? from, DateOnly? to, string? q)
{
    var query = _db.Transactions
        .Include(t => t.Account).ThenInclude(a => a.Currency)
        .Include(t => t.Category).ThenInclude(c => c.CategoryType)
        .AsQueryable();

    if (accountId.HasValue)
        query = query.Where(t => t.AccountId == accountId.Value);
    if (from.HasValue)
        query = query.Where(t => t.Date >= from.Value);
    if (to.HasValue)
        query = query.Where(t => t.Date <= to.Value);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var pattern = $"%{q}%";
        query = query.Where(t =>
            EF.Functions.ILike(t.Description ?? "", pattern) ||
            EF.Functions.ILike(t.Category.Name, pattern));
    }

    return await query.Select(t => new MovementListItemViewModel
    {
        // ... existing projection unchanged ...
    }).ToListAsync();
}

private async Task<List<MovementListItemViewModel>> QueryTransfers(
    Guid? accountId, DateOnly? from, DateOnly? to, string? q)
{
    var query = _db.Transfers
        .Include(t => t.SourceAccount).ThenInclude(a => a.Currency)
        .Include(t => t.DestAccount)
        .AsQueryable();

    if (accountId.HasValue)
        query = query.Where(t => t.SourceAccountId == accountId.Value || t.DestAccountId == accountId.Value);
    if (from.HasValue)
        query = query.Where(t => t.Date >= from.Value);
    if (to.HasValue)
        query = query.Where(t => t.Date <= to.Value);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var pattern = $"%{q}%";
        // Transfers have no category; description-only match
        query = query.Where(t => EF.Functions.ILike(t.Description ?? "", pattern));
    }

    return await query.Select(t => new MovementListItemViewModel
    {
        // ... existing projection unchanged ...
    }).ToListAsync();
}

private async Task<List<MovementListItemViewModel>> QueryLiabilityPayments(
    Guid? accountId, DateOnly? from, DateOnly? to, string? q)
{
    var query = _db.LiabilityPayments
        .Include(p => p.AssetAccount)
        .Include(p => p.LiabilityAccount)
        .AsQueryable();

    if (accountId.HasValue)
        query = query.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
    if (from.HasValue)
        query = query.Where(p => p.Date >= from.Value);
    if (to.HasValue)
        query = query.Where(p => p.Date <= to.Value);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var pattern = $"%{q}%";
        // Liability payments have no category; description-only match
        query = query.Where(p => EF.Functions.ILike(p.Description ?? "", pattern));
    }

    return await query.Select(p => new MovementListItemViewModel
    {
        // ... existing projection unchanged ...
    }).ToListAsync();
}
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetRecent_FiltersByDescriptionOrCategoryName|FullyQualifiedName~Count_RespectsQFilter"`
Expected: PASS.

- [ ] **Step 6: Run the full backend suite to confirm no regressions**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass (existing tests use the default `q = null` and get the same behavior as before).

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/IMovementService.cs ProjectCeres/Services/MovementService.cs ProjectCeres.Tests/Integration/MovementServiceTests.cs
git commit -m "feat(movements): add q text-search to IMovementService"
```

---

### Task 2: Add `MovementsPageDto` and `MovementListItemDto`

**Files:**
- Create: `ProjectCeres/ViewModels/MovementsApiDtos.cs`

- [ ] **Step 1: Create the DTOs**

Create `ProjectCeres/ViewModels/MovementsApiDtos.cs`:

```csharp
namespace ProjectCeres.ViewModels;

public record MovementListItemDto(
    Guid Id,
    string MovementType,
    DateOnly Date,
    decimal Amount,
    string CurrencyCode,
    string CurrencySymbol,
    string? Description,
    bool IsCleared,
    string? AccountName,
    string? CategoryName,
    string? CategoryTypeName,
    string? SourceAccountName,
    string? DestAccountName,
    string? AssetAccountName,
    string? LiabilityAccountName);

public record MovementsPageDto(
    IReadOnlyList<MovementListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/ViewModels/MovementsApiDtos.cs
git commit -m "feat(movements): add MovementsPageDto and MovementListItemDto"
```

---

### Task 3: Add `GET /api/movements` endpoint

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MovementsApiController.cs`
- Modify: `ProjectCeres.Tests/Integration/MovementsApiTests.cs`

The existing controller already has `PATCH {id}/cleared`. We're adding a `GET ""` action and the constructor needs `IMovementService` + `IMovementListMapper` (or inline mapping — we'll inline it).

- [ ] **Step 1: Write a failing integration test for the GET**

Append to `ProjectCeres.Tests/Integration/MovementsApiTests.cs` (inside the existing class). Reuse the existing seed helpers:

```csharp
[Fact]
public async Task GetMovements_Returns200_WithWrappedShape()
{
    var response = await _client.GetAsync("/api/movements?pageSize=50");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);

    body.TryGetProperty("items", out var items).Should().BeTrue();
    items.ValueKind.Should().Be(JsonValueKind.Array);

    body.TryGetProperty("totalCount", out var totalCount).Should().BeTrue();
    totalCount.ValueKind.Should().Be(JsonValueKind.Number);

    body.TryGetProperty("page", out var page).Should().BeTrue();
    page.GetInt32().Should().Be(1);

    body.TryGetProperty("pageSize", out var pageSize).Should().BeTrue();
    pageSize.GetInt32().Should().Be(50);

    foreach (var item in items.EnumerateArray())
    {
        item.TryGetProperty("id", out _).Should().BeTrue();
        item.TryGetProperty("movementType", out _).Should().BeTrue();
        item.TryGetProperty("date", out _).Should().BeTrue();
        item.TryGetProperty("amount", out _).Should().BeTrue();
        item.TryGetProperty("currencyCode", out _).Should().BeTrue();
        item.TryGetProperty("currencySymbol", out _).Should().BeTrue();
        item.TryGetProperty("isCleared", out _).Should().BeTrue();
    }
}

[Fact]
public async Task GetMovements_RespectsPaginationParams()
{
    var response = await _client.GetAsync("/api/movements?page=2&pageSize=10");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("page").GetInt32().Should().Be(2);
    body.GetProperty("pageSize").GetInt32().Should().Be(10);
    body.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(10);
}
```

- [ ] **Step 2: Run the test to verify it fails (404)**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetMovements_Returns200_WithWrappedShape"`
Expected: FAIL — `GET /api/movements` returns 404 (only PATCH is registered today).

- [ ] **Step 3: Add `IMovementService` to the controller and the GET action**

Replace the contents of `ProjectCeres/Controllers/Api/MovementsApiController.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/movements")]
public class MovementsApiController(
    IMovementService movementService,
    ITransactionService transactionService,
    ITransferService transferService,
    ILiabilityPaymentService liabilityPaymentService) : ControllerBase
{
    public record ClearRequest(string Type, bool Cleared);

    [HttpGet]
    public async Task<MovementsPageDto> GetMovements(
        [FromQuery] string? q = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var offset = (page - 1) * pageSize;

        var items = await movementService.GetRecentAsync(accountId, from, to, pageSize, offset, q);
        var total = await movementService.CountAsync(accountId, from, to, q);

        var dtoItems = items.Select(MapToDto).ToList();

        return new MovementsPageDto(dtoItems, total, page, pageSize);
    }

    [HttpPatch("{id:guid}/cleared")]
    public async Task<IActionResult> PatchCleared(Guid id, [FromBody] ClearRequest request)
    {
        switch (request.Type.ToLowerInvariant())
        {
            case "transaction":
                var tx = await transactionService.GetByIdForEditAsync(id);
                if (tx is null) return NotFound();
                await transactionService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            case "transfer":
                var tr = await transferService.GetByIdAsync(id);
                if (tr is null) return NotFound();
                await transferService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            case "liabilitypayment":
                var lp = await liabilityPaymentService.GetByIdAsync(id);
                if (lp is null) return NotFound();
                await liabilityPaymentService.MarkClearedAsync(id, request.Cleared);
                return Ok();

            default:
                return BadRequest(new { error = new { code = "INVALID_TYPE", message = "Type must be 'transaction', 'transfer', or 'liabilitypayment'." } });
        }
    }

    private static MovementListItemDto MapToDto(MovementListItemViewModel m)
    {
        // CurrencyCode isn't on the existing ViewModel; derive from CurrencySymbol mapping
        // is not viable. Instead, return CurrencySymbol-only and leave CurrencyCode empty for now;
        // we'll enrich the ViewModel in a follow-up if needed. For this slice, return symbol as both.
        // (CurrencyCode is consumed by the SPA only for the tooltip — symbol is enough for display.)
        return new MovementListItemDto(
            Id: m.Id,
            MovementType: m.MovementType.ToString(),
            Date: m.Date,
            Amount: m.Amount,
            CurrencyCode: "", // intentionally empty; not displayed in this slice
            CurrencySymbol: m.CurrencySymbol ?? "",
            Description: m.Description,
            IsCleared: m.IsCleared,
            AccountName: m.AccountName,
            CategoryName: m.CategoryName,
            CategoryTypeName: m.CategoryTypeName,
            SourceAccountName: m.SourceAccountName,
            DestAccountName: m.DestAccountName,
            AssetAccountName: m.AssetAccountName,
            LiabilityAccountName: m.LiabilityAccountName);
    }
}
```

(Note: `MovementsApiController` previously had only two services injected; we're adding `movementService` and `liabilityPaymentService`. The PATCH switch gains a `liabilitypayment` case — see Task 4 for `ILiabilityPaymentService.MarkClearedAsync` which we add next.)

- [ ] **Step 4: Run the new GET tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetMovements"`
Expected: PASS.

- [ ] **Step 5: Verify all existing tests still pass**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass (PATCH tests in `MovementsApiTests` still pass — only switch wording changed).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/MovementsApiController.cs ProjectCeres.Tests/Integration/MovementsApiTests.cs
git commit -m "feat(movements): add GET /api/movements with paged DTO"
```

---

### Task 4: Add `MarkClearedAsync` to `ILiabilityPaymentService` (PATCH support)

**Files:**
- Modify: `ProjectCeres/Services/ILiabilityPaymentService.cs`
- Modify: `ProjectCeres/Services/LiabilityPaymentService.cs`
- Modify: `ProjectCeres.Tests/Integration/MovementsApiTests.cs` — add liability-payment PATCH test

- [ ] **Step 1: Write the failing integration test**

Append to `ProjectCeres.Tests/Integration/MovementsApiTests.cs` (reuse the existing seed helpers; the file already seeds liability payments for other tests):

```csharp
[Fact]
public async Task PatchCleared_TogglesLiabilityPayment()
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var asset = SeedAssetAccount(db, "Checking-LP");
    var liability = SeedLiabilityAccount(db, "Credit Card-LP");
    var paymentId = SeedLiabilityPayment(db, asset.Id, liability.Id, amount: 50, isCleared: false);
    await db.SaveChangesAsync();
    _seededPaymentIds.Add(paymentId);

    var body = new StringContent(
        JsonSerializer.Serialize(new { type = "liabilitypayment", cleared = true }),
        Encoding.UTF8, "application/json");

    var response = await _client.PatchAsync($"/api/movements/{paymentId}/cleared", body);

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    using var verifyScope = _factory.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var updated = await verifyDb.LiabilityPayments.FindAsync(paymentId);
    updated!.IsCleared.Should().BeTrue();
}
```

(The helpers `SeedLiabilityAccount`, `SeedLiabilityPayment`, and the `_seededPaymentIds` list may need to be added to the test class's helper section if not already present. Mirror the pattern of `_seededTransactionIds`/`_seededTransferIds` and the `SeedTransaction`/`SeedTransfer` helpers already in the file.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~PatchCleared_TogglesLiabilityPayment"`
Expected: FAIL — `ILiabilityPaymentService.MarkClearedAsync` doesn't exist (compile error).

- [ ] **Step 3: Add `MarkClearedAsync` to the interface**

In `ProjectCeres/Services/ILiabilityPaymentService.cs`, add to the interface:

```csharp
Task MarkClearedAsync(Guid id, bool cleared);
```

- [ ] **Step 4: Implement on the service**

In `ProjectCeres/Services/LiabilityPaymentService.cs`, add:

```csharp
public async Task MarkClearedAsync(Guid id, bool cleared)
{
    var payment = await db.LiabilityPayments.FindAsync(id)
        ?? throw new InvalidOperationException($"Liability payment {id} not found.");

    payment.IsCleared = cleared;
    await db.SaveChangesAsync();
}
```

(Mirror the pattern in `TransactionService.MarkClearedAsync` and `TransferService.MarkClearedAsync`.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~PatchCleared_TogglesLiabilityPayment"`
Expected: PASS.

- [ ] **Step 6: Run all backend tests**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/ILiabilityPaymentService.cs ProjectCeres/Services/LiabilityPaymentService.cs ProjectCeres.Tests/Integration/MovementsApiTests.cs
git commit -m "feat(movements): support liability-payment cleared toggle via PATCH"
```

---

### Task 5: Add quick-add request DTOs

**Files:**
- Create: `ProjectCeres/ViewModels/QuickAddDtos.cs`

- [ ] **Step 1: Create the DTOs**

Create `ProjectCeres/ViewModels/QuickAddDtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CreateTransactionRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public Guid? CategoryId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

public class CreateTransferRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select a source account.")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Please select a destination account.")]
    public Guid? DestAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

public class CreateLiabilityPaymentRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an asset account.")]
    public Guid? AssetAccountId { get; set; }

    [Required(ErrorMessage = "Please select a liability account.")]
    public Guid? LiabilityAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/ViewModels/QuickAddDtos.cs
git commit -m "feat(quickadd): add Create*Request DTOs with data annotations"
```

---

### Task 6: Add `POST /api/transactions` controller

> **CORRECTION (post-implementation):** Validation responses are **422 Unprocessable Entity**, not 400. `Program.cs` configures `InvalidModelStateResponseFactory` to return 422 for all auto-validation failures from `[ApiController]`. Tests must assert `HttpStatusCode.UnprocessableEntity`. The manual `if (!ModelState.IsValid) return ValidationProblem(ModelState);` guard is also unnecessary — `[ApiController]` runs that automatically. **Also:** the `Account` model has no `OpeningBalance`, `OpeningBalanceDate`, or `CreatedAt` fields — seed helpers must only set `Id`, `Name`, `AccountTypeId`, `CurrencyId`, `IsActive`. (Both corrections were applied during implementation.)

**Files:**
- Create: `ProjectCeres/Controllers/Api/TransactionsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/TransactionsApiTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ProjectCeres.Tests/Integration/TransactionsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class TransactionsApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransactionsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
        {
            var rows = await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ToListAsync();
            db.Transactions.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAssetAccount(AppDbContext db, string name)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountTypeId = 1,
            CurrencyId = 1,
            IsActive = true,
            OpeningBalance = 0,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesTransaction()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = SeedAssetAccount(db, "Checking-Tx");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 25.50m,
            accountId = account.Id,
            categoryId = HousingCategoryId,
            description = "Test rent"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        var newId = idProp.GetGuid();
        _seededTransactionIds.Add(newId);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await verifyDb.Transactions.FindAsync(newId);
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(25.50m);
        saved.Description.Should().Be("Test rent");
    }

    [Fact]
    public async Task Post_Returns400_WhenAmountIsZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = SeedAssetAccount(db, "Checking-Tx-Bad");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 0m,
            accountId = account.Id,
            categoryId = HousingCategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns400_WhenAccountIdMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date = "2026-04-15",
            amount = 10m,
            categoryId = HousingCategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (404 then nothing)**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsApiTests"`
Expected: FAIL on `Post_Returns201` (404 Not Found — endpoint doesn't exist).

- [ ] **Step 3: Create the controller**

Create `ProjectCeres/Controllers/Api/TransactionsApiController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transactions")]
public class TransactionsApiController(ITransactionService transactionService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var vm = new TransactionCreateViewModel
        {
            TransactionType = "Regular",
            Date = request.Date,
            Amount = request.Amount,
            AccountId = request.AccountId,
            CategoryId = request.CategoryId,
            Description = request.Description
        };

        var transaction = await transactionService.CreateAsync(vm);

        return Created($"/api/transactions/{transaction.Id}", new { id = transaction.Id });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsApiTests"`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransactionsApiController.cs ProjectCeres.Tests/Integration/TransactionsApiTests.cs
git commit -m "feat(transactions): add POST /api/transactions for quick-add"
```

---

### Task 7: Add `POST /api/transfers` and `POST /api/liability-payments` controllers

> **CORRECTION (post-implementation):** Same 422 / Account-seed corrections as Task 6 apply here. Additionally: a manual `ValidationProblem(ModelState)` call returns 400 (the default ProblemDetails behavior) — it does NOT go through the `InvalidModelStateResponseFactory`. For cross-field validation (e.g., source==destination) and caught service exceptions, return `UnprocessableEntity(...)` directly with the same JSON shape as the factory produces, so the response is consistently 422. (Applied during implementation.)

**Files:**
- Create: `ProjectCeres/Controllers/Api/TransfersApiController.cs`
- Create: `ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/TransfersApiTests.cs`
- Create: `ProjectCeres.Tests/Integration/LiabilityPaymentsApiTests.cs`

- [ ] **Step 1: Write the TransfersApi failing tests**

Create `ProjectCeres.Tests/Integration/TransfersApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class TransfersApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransfersApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransferIds.Count > 0)
        {
            var rows = await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ToListAsync();
            db.Transfers.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAccount(AppDbContext db, string name, int accountTypeId = 1, int currencyId = 1)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountTypeId = accountTypeId,
            CurrencyId = currencyId,
            IsActive = true,
            OpeningBalance = 0,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesTransfer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var src = SeedAccount(db, "Src-Tr");
        var dst = SeedAccount(db, "Dst-Tr");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transfers", new
        {
            date = "2026-04-15",
            amount = 100m,
            sourceAccountId = src.Id,
            destAccountId = dst.Id,
            description = "Test transfer"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("id").GetGuid();
        _seededTransferIds.Add(newId);
    }

    [Fact]
    public async Task Post_Returns400_WhenSourceEqualsDest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var src = SeedAccount(db, "Src-Same");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transfers", new
        {
            date = "2026-04-15",
            amount = 100m,
            sourceAccountId = src.Id,
            destAccountId = src.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Write the LiabilityPaymentsApi failing tests**

Create `ProjectCeres.Tests/Integration/LiabilityPaymentsApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class LiabilityPaymentsApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededPaymentIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public LiabilityPaymentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededPaymentIds.Count > 0)
        {
            var rows = await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ToListAsync();
            db.LiabilityPayments.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAccount(AppDbContext db, string name, int accountTypeId)
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountTypeId = accountTypeId,
            CurrencyId = 1,
            IsActive = true,
            OpeningBalance = 0,
            OpeningBalanceDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesLiabilityPayment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = SeedAccount(db, "Asset-LP", accountTypeId: 1);
        var liability = SeedAccount(db, "Liability-LP", accountTypeId: 2);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/liability-payments", new
        {
            date = "2026-04-15",
            amount = 75m,
            assetAccountId = asset.Id,
            liabilityAccountId = liability.Id,
            description = "Card payment"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("id").GetGuid();
        _seededPaymentIds.Add(newId);
    }

    [Fact]
    public async Task Post_Returns400_WhenAmountIsZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = SeedAccount(db, "Asset-LP-Bad", accountTypeId: 1);
        var liability = SeedAccount(db, "Liability-LP-Bad", accountTypeId: 2);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/liability-payments", new
        {
            date = "2026-04-15",
            amount = 0m,
            assetAccountId = asset.Id,
            liabilityAccountId = liability.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransfersApiTests|FullyQualifiedName~LiabilityPaymentsApiTests"`
Expected: FAIL — endpoints don't exist (404).

- [ ] **Step 4: Create `TransfersApiController`**

Create `ProjectCeres/Controllers/Api/TransfersApiController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/transfers")]
public class TransfersApiController(ITransferService transferService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransferRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (request.SourceAccountId == request.DestAccountId)
        {
            ModelState.AddModelError(nameof(request.DestAccountId), "Source and destination accounts must be different.");
            return ValidationProblem(ModelState);
        }

        var vm = new TransferCreateViewModel
        {
            Date = request.Date,
            Amount = request.Amount,
            SourceAccountId = request.SourceAccountId,
            DestAccountId = request.DestAccountId,
            Description = request.Description
        };

        try
        {
            var transfer = await transferService.CreateAsync(vm);
            return Created($"/api/transfers/{transfer.Id}", new { id = transfer.Id });
        }
        catch (InvalidOperationException ex)
        {
            // TransferService throws on cross-currency / opening-balance violations
            ModelState.AddModelError("", ex.Message);
            return ValidationProblem(ModelState);
        }
    }
}
```

- [ ] **Step 5: Create `LiabilityPaymentsApiController`**

Create `ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/liability-payments")]
public class LiabilityPaymentsApiController(ILiabilityPaymentService liabilityPaymentService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLiabilityPaymentRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var vm = new TransactionCreateViewModel
        {
            TransactionType = "LiabilityPayment",
            Date = request.Date,
            Amount = request.Amount,
            AccountId = request.AssetAccountId,
            LiabilityAccountId = request.LiabilityAccountId,
            Description = request.Description
        };

        var payment = await liabilityPaymentService.CreateAsync(vm);
        return Created($"/api/liability-payments/{payment.Id}", new { id = payment.Id });
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransfersApiTests|FullyQualifiedName~LiabilityPaymentsApiTests"`
Expected: All pass.

- [ ] **Step 7: Run full backend suite**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransfersApiController.cs ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs ProjectCeres.Tests/Integration/TransfersApiTests.cs ProjectCeres.Tests/Integration/LiabilityPaymentsApiTests.cs
git commit -m "feat(quickadd): add POST /api/transfers and /api/liability-payments"
```

---

### Task 8: Add combobox helper endpoints (`/api/accounts/active`, `/api/categories/active`)

**Files:**
- Create: `ProjectCeres/ViewModels/ComboboxOptionDtos.cs`
- Create: `ProjectCeres/Controllers/Api/AccountsApiController.cs`
- Create: `ProjectCeres/Controllers/Api/CategoriesApiController.cs`
- Create: `ProjectCeres.Tests/Integration/ComboboxApiTests.cs`

- [ ] **Step 1: Create the option DTOs**

Create `ProjectCeres/ViewModels/ComboboxOptionDtos.cs`:

```csharp
namespace ProjectCeres.ViewModels;

public record AccountOptionDto(
    Guid Id,
    string Name,
    string CurrencyCode,
    string CurrencySymbol,
    string AccountTypeName);

public record CategoryOptionDto(
    Guid Id,
    string Name,
    string CategoryTypeName);
```

- [ ] **Step 2: Write failing integration tests**

Create `ProjectCeres.Tests/Integration/ComboboxApiTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class ComboboxApiTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetActiveAccounts_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/accounts/active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("currencyCode", out _).Should().BeTrue();
            item.TryGetProperty("currencySymbol", out _).Should().BeTrue();
            item.TryGetProperty("accountTypeName", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetActiveCategories_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/categories/active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("categoryTypeName", out _).Should().BeTrue();
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ComboboxApiTests"`
Expected: FAIL (404).

- [ ] **Step 4: Create `AccountsApiController`**

Create `ProjectCeres/Controllers/Api/AccountsApiController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/accounts")]
public class AccountsApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IReadOnlyList<AccountOptionDto>> GetActive()
    {
        return await db.Accounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new AccountOptionDto(
                a.Id,
                a.Name,
                a.Currency.Code,
                a.Currency.Symbol,
                a.AccountType.Name))
            .ToListAsync();
    }
}
```

- [ ] **Step 5: Create `CategoriesApiController`**

Create `ProjectCeres/Controllers/Api/CategoriesApiController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/categories")]
public class CategoriesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IReadOnlyList<CategoryOptionDto>> GetActive()
    {
        return await db.Categories
            .Where(c => c.IsActive && !c.IsSystem)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOptionDto(
                c.Id,
                c.Name,
                c.CategoryType.Name))
            .ToListAsync();
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~ComboboxApiTests"`
Expected: PASS.

- [ ] **Step 7: Run full backend suite**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/ViewModels/ComboboxOptionDtos.cs ProjectCeres/Controllers/Api/AccountsApiController.cs ProjectCeres/Controllers/Api/CategoriesApiController.cs ProjectCeres.Tests/Integration/ComboboxApiTests.cs
git commit -m "feat(api): add /api/accounts/active and /api/categories/active for comboboxes"
```

---

### Task 9: Install Sonner and mount `<Toaster />`

**Files:**
- Create (via shadcn): `ProjectCeres.Client/src/components/ui/sonner.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/AppLayout.tsx`

- [ ] **Step 1: Install Sonner via shadcn**

Run from `ProjectCeres.Client/`:

```bash
pnpm dlx shadcn@latest add sonner
```

Expected: creates `src/components/ui/sonner.tsx`. Confirms Sonner is added to `package.json` dependencies.

- [ ] **Step 2: Mount `<Toaster />` in AppLayout**

In `ProjectCeres.Client/src/app/layout/AppLayout.tsx`, add the import at the top:

```tsx
import { Toaster } from '@/components/ui/sonner';
```

And add `<Toaster />` as the last child inside the outermost div (just before the closing `</div>`):

```tsx
return (
  <div className="grid h-screen grid-rows-[3.5rem_1fr] bg-background text-foreground">
    {/* ... existing content ... */}
    <Toaster />
  </div>
);
```

- [ ] **Step 3: Verify type-check + tests still pass**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No TS errors. All tests pass (existing tests don't reference Toaster).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/sonner.tsx ProjectCeres.Client/src/app/layout/AppLayout.tsx ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml
git commit -m "feat(toast): adopt Sonner for SPA-wide toast notifications"
```

---

### Task 10: Add `useDebounced` hook

**Files:**
- Create: `ProjectCeres.Client/src/app/lib/use-debounced.ts`
- Create: `ProjectCeres.Client/src/app/lib/use-debounced.test.ts`

- [ ] **Step 1: Write failing tests**

Create `ProjectCeres.Client/src/app/lib/use-debounced.test.ts`:

```ts
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useDebounced } from './use-debounced';

describe('useDebounced', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('returns the initial value immediately', () => {
    const { result } = renderHook(() => useDebounced('a', 300));
    expect(result.current).toBe('a');
  });

  it('updates the value after the delay elapses', () => {
    const { result, rerender } = renderHook(({ value }) => useDebounced(value, 300), {
      initialProps: { value: 'a' },
    });

    rerender({ value: 'ab' });
    expect(result.current).toBe('a');

    act(() => { vi.advanceTimersByTime(300); });
    expect(result.current).toBe('ab');
  });

  it('cancels the pending update when the value changes again before the delay elapses', () => {
    const { result, rerender } = renderHook(({ value }) => useDebounced(value, 300), {
      initialProps: { value: 'a' },
    });

    rerender({ value: 'ab' });
    act(() => { vi.advanceTimersByTime(200); });
    rerender({ value: 'abc' });
    act(() => { vi.advanceTimersByTime(200); });
    expect(result.current).toBe('a'); // still original — total time since last change is 200ms

    act(() => { vi.advanceTimersByTime(100); });
    expect(result.current).toBe('abc');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/use-debounced.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the hook**

Create `ProjectCeres.Client/src/app/lib/use-debounced.ts`:

```ts
import { useEffect, useState } from 'react';

/**
 * Returns `value` delayed by `delayMs`. Each new `value` resets the timer.
 * Useful for debouncing search inputs before they propagate to URL state or API calls.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/use-debounced.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/lib/use-debounced.ts ProjectCeres.Client/src/app/lib/use-debounced.test.ts
git commit -m "feat(lib): add useDebounced hook for search input"
```

---

### Task 11: Add `movements-api.ts` (frontend DTOs + URL constants)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/movements-api.ts`

- [ ] **Step 1: Create the file**

Create `ProjectCeres.Client/src/app/features/movements/movements-api.ts`:

```ts
export const MOVEMENTS_URL                     = '/api/movements';
export const MOVEMENTS_CLEARED_URL             = (id: string) => `/api/movements/${id}/cleared`;
export const TRANSACTIONS_CREATE_URL           = '/api/transactions';
export const TRANSFERS_CREATE_URL              = '/api/transfers';
export const LIABILITY_PAYMENTS_CREATE_URL     = '/api/liability-payments';
export const ACCOUNTS_ACTIVE_URL               = '/api/accounts/active';
export const CATEGORIES_ACTIVE_URL             = '/api/categories/active';

export type MovementType = 'Transaction' | 'Transfer' | 'LiabilityPayment';

export type MovementListItemDto = {
  id: string;
  movementType: MovementType;
  date: string; // "yyyy-MM-dd"
  amount: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isCleared: boolean;
  accountName: string | null;
  categoryName: string | null;
  categoryTypeName: string | null;
  sourceAccountName: string | null;
  destAccountName: string | null;
  assetAccountName: string | null;
  liabilityAccountName: string | null;
};

export type MovementsPageDto = {
  items: MovementListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type CreateTransactionRequest = {
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
};

export type CreateTransferRequest = {
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
};

export type CreateLiabilityPaymentRequest = {
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
};

export type AccountOptionDto = {
  id: string;
  name: string;
  currencyCode: string;
  currencySymbol: string;
  accountTypeName: string;
};

export type CategoryOptionDto = {
  id: string;
  name: string;
  categoryTypeName: string;
};

export type ServerValidationProblem = {
  errors?: Record<string, string[]>;
};
```

- [ ] **Step 2: Verify type-check**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/movements-api.ts
git commit -m "feat(movements): add typed DTOs and URL constants"
```

---

### Task 12: Build `AccountCombobox` component

**Files:**
- Create: `ProjectCeres.Client/src/app/components/AccountCombobox.tsx`
- Create: `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx`

- [ ] **Step 1: Install shadcn Command + Popover (if not already present)**

Check if both are installed:

Run: `ls ProjectCeres.Client/src/components/ui/ | grep -E '^(command|popover)\.tsx$'`

If either is missing, run from `ProjectCeres.Client/`:

```bash
pnpm dlx shadcn@latest add command popover
```

(Skip the install if both files already exist.)

- [ ] **Step 2: Write the failing test**

Create `ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx`:

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AccountCombobox } from './AccountCombobox';
import type { AccountOptionDto } from '../features/movements/movements-api';

const accounts: AccountOptionDto[] = [
  { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
];

describe('AccountCombobox', () => {
  it('shows the placeholder when no account is selected', () => {
    render(<AccountCombobox accounts={accounts} value={null} onChange={vi.fn()} placeholder="Select account" />);
    expect(screen.getByText('Select account')).toBeInTheDocument();
  });

  it('shows the selected account name when value is set', () => {
    render(<AccountCombobox accounts={accounts} value="a2" onChange={vi.fn()} placeholder="Select account" />);
    expect(screen.getByText('Savings')).toBeInTheDocument();
  });

  it('calls onChange with the account id when an option is picked', () => {
    const onChange = vi.fn();
    render(<AccountCombobox accounts={accounts} value={null} onChange={onChange} placeholder="Select account" />);

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Checking'));

    expect(onChange).toHaveBeenCalledWith('a1');
  });

  it('respects the filter prop to narrow options', () => {
    render(
      <AccountCombobox
        accounts={accounts}
        value={null}
        onChange={vi.fn()}
        placeholder="Select"
        filter={(a) => a.accountTypeName !== 'Liability'}
      />,
    );
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Checking')).toBeInTheDocument();
    expect(screen.getByText('Savings')).toBeInTheDocument();
    expect(screen.queryByText('Credit Card')).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/AccountCombobox.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 4: Implement `AccountCombobox`**

Create `ProjectCeres.Client/src/app/components/AccountCombobox.tsx`:

```tsx
import { Check, ChevronsUpDown } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { AccountOptionDto } from '../features/movements/movements-api';

type Props = {
  accounts: AccountOptionDto[];
  value: string | null;
  onChange: (accountId: string) => void;
  placeholder: string;
  filter?: (account: AccountOptionDto) => boolean;
  disabled?: boolean;
};

export function AccountCombobox({ accounts, value, onChange, placeholder, filter, disabled }: Props) {
  const [open, setOpen] = useState(false);
  const filtered = filter ? accounts.filter(filter) : accounts;
  const selected = accounts.find((a) => a.id === value) ?? null;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className="w-full justify-between"
          >
            {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search accounts…" />
          <CommandList>
            <CommandEmpty>No accounts found.</CommandEmpty>
            <CommandGroup>
              {filtered.map((account) => (
                <CommandItem
                  key={account.id}
                  value={account.name}
                  onSelect={() => {
                    onChange(account.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === account.id ? 'opacity-100' : 'opacity-0')} />
                  {account.name}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/AccountCombobox.test.tsx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/components/AccountCombobox.tsx ProjectCeres.Client/src/app/components/AccountCombobox.test.tsx ProjectCeres.Client/src/components/ui/command.tsx ProjectCeres.Client/src/components/ui/popover.tsx ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml
git commit -m "feat(components): add AccountCombobox searchable selector"
```

(If `command.tsx` and `popover.tsx` already existed, the `git add` will silently no-op for those.)

---

### Task 13: Build `CategoryCombobox` component

**Files:**
- Create: `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`
- Create: `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx`:

```tsx
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CategoryCombobox } from './CategoryCombobox';
import type { CategoryOptionDto } from '../features/movements/movements-api';

const categories: CategoryOptionDto[] = [
  { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
  { id: 'c2', name: 'Groceries', categoryTypeName: 'Expense' },
];

describe('CategoryCombobox', () => {
  it('shows the placeholder when no category is selected', () => {
    render(<CategoryCombobox categories={categories} value={null} onChange={vi.fn()} placeholder="Select category" />);
    expect(screen.getByText('Select category')).toBeInTheDocument();
  });

  it('shows the selected category name', () => {
    render(<CategoryCombobox categories={categories} value="c1" onChange={vi.fn()} placeholder="Select" />);
    expect(screen.getByText('Salary')).toBeInTheDocument();
  });

  it('calls onChange with the id when an option is picked', () => {
    const onChange = vi.fn();
    render(<CategoryCombobox categories={categories} value={null} onChange={onChange} placeholder="Select" />);

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Groceries'));

    expect(onChange).toHaveBeenCalledWith('c2');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/CategoryCombobox.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `CategoryCombobox`**

Create `ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`:

```tsx
import { Check, ChevronsUpDown } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { CategoryOptionDto } from '../features/movements/movements-api';

type Props = {
  categories: CategoryOptionDto[];
  value: string | null;
  onChange: (categoryId: string) => void;
  placeholder: string;
  disabled?: boolean;
};

export function CategoryCombobox({ categories, value, onChange, placeholder, disabled }: Props) {
  const [open, setOpen] = useState(false);
  const selected = categories.find((c) => c.id === value) ?? null;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className="w-full justify-between"
          >
            {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search categories…" />
          <CommandList>
            <CommandEmpty>No categories found.</CommandEmpty>
            <CommandGroup>
              {categories.map((category) => (
                <CommandItem
                  key={category.id}
                  value={category.name}
                  onSelect={() => {
                    onChange(category.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === category.id ? 'opacity-100' : 'opacity-0')} />
                  <span className="flex-1">{category.name}</span>
                  <span className="text-xs text-muted-foreground">{category.categoryTypeName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/CategoryCombobox.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/components/CategoryCombobox.tsx ProjectCeres.Client/src/app/components/CategoryCombobox.test.tsx
git commit -m "feat(components): add CategoryCombobox searchable selector"
```

---

### Task 14: Build `QuickAddModal` (3 tabs, inline errors, toast)

**Files:**
- Create: `ProjectCeres.Client/src/app/components/QuickAddModal.tsx`
- Create: `ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx`

This is the largest single file in the slice (~250 lines). It pulls together comboboxes, server-error mapping, and Sonner toasts. If the file grows beyond ~300 lines during implementation, stop and report `DONE_WITH_CONCERNS` so we can split per-tab.

- [ ] **Step 1: Write failing tests**

Create `ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { QuickAddModal } from './QuickAddModal';

// Mock Sonner so we can assert toast calls
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

const accountsResponse = {
  ok: true,
  json: async () => [
    { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
  ],
};

const categoriesResponse = {
  ok: true,
  json: async () => [
    { id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' },
  ],
};

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url.includes('/api/accounts/active')) return Promise.resolve(accountsResponse);
    if (url.includes('/api/categories/active')) return Promise.resolve(categoriesResponse);
    return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-id' }) });
  });
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('QuickAddModal', () => {
  it('renders three tabs when open', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /transaction/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /transfer/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /liability payment/i })).toBeInTheDocument();
    });
  });

  it('Transaction tab POSTs to /api/transactions on submit', async () => {
    const onOpenChange = vi.fn();
    const onSaved = vi.fn();
    render(<QuickAddModal open={true} onOpenChange={onOpenChange} onSaved={onSaved} />);

    await waitFor(() => screen.getByRole('tab', { name: /transaction/i }));

    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '25.50' } });
    // Pick first account
    fireEvent.click(screen.getAllByRole('combobox')[0]);
    fireEvent.click(await screen.findByText('Checking'));
    // Pick first category
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByText('Groceries'));

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      const txCall = mockFetch.mock.calls.find((call) => call[0] === '/api/transactions');
      expect(txCall).toBeDefined();
    });

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
      expect(onSaved).toHaveBeenCalled();
    });
  });

  it('Transfer tab shows source and destination account fields, no category', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    fireEvent.click(await screen.findByRole('tab', { name: /transfer/i }));

    expect(screen.getByText(/source account/i)).toBeInTheDocument();
    expect(screen.getByText(/destination account/i)).toBeInTheDocument();
    expect(screen.queryByText(/^category$/i)).not.toBeInTheDocument();
  });

  it('Liability Payment tab filters destination to liability accounts', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    fireEvent.click(await screen.findByRole('tab', { name: /liability payment/i }));

    // Destination combobox should only show liability accounts (Credit Card)
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    expect(await screen.findByText('Credit Card')).toBeInTheDocument();
    expect(screen.queryByText('Checking')).not.toBeInTheDocument();
  });

  it('renders inline errors on 422 ValidationProblem', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.includes('/api/accounts/active')) return Promise.resolve(accountsResponse);
      if (url.includes('/api/categories/active')) return Promise.resolve(categoriesResponse);
      return Promise.resolve({
        ok: false,
        status: 422,
        json: async () => ({ errors: { Amount: ['Amount must be greater than zero.'] } }),
      });
    });

    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);

    await waitFor(() => screen.getByRole('tab', { name: /transaction/i }));
    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '0' } });
    fireEvent.click(screen.getAllByRole('combobox')[0]);
    fireEvent.click(await screen.findByText('Checking'));
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByText('Groceries'));

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText('Amount must be greater than zero.')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Install shadcn Tabs (if not present)**

Check: `ls ProjectCeres.Client/src/components/ui/tabs.tsx 2>/dev/null`. If missing, run from `ProjectCeres.Client/`:

```bash
pnpm dlx shadcn@latest add tabs
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/QuickAddModal.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 4: Implement `QuickAddModal`**

Create `ProjectCeres.Client/src/app/components/QuickAddModal.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { AccountCombobox } from './AccountCombobox';
import { CategoryCombobox } from './CategoryCombobox';
import {
  ACCOUNTS_ACTIVE_URL,
  CATEGORIES_ACTIVE_URL,
  LIABILITY_PAYMENTS_CREATE_URL,
  TRANSACTIONS_CREATE_URL,
  TRANSFERS_CREATE_URL,
  type AccountOptionDto,
  type CategoryOptionDto,
} from '../features/movements/movements-api';

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSaved?: () => void;
};

type TabKey = 'transaction' | 'transfer' | 'liabilityPayment';
type FieldErrors = Record<string, string>;

const todayIso = () => new Date().toISOString().slice(0, 10);

export function QuickAddModal({ open, onOpenChange, onSaved }: Props) {
  const [tab, setTab] = useState<TabKey>('transaction');
  const [accounts, setAccounts] = useState<AccountOptionDto[]>([]);
  const [categories, setCategories] = useState<CategoryOptionDto[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});

  // Shared fields
  const [date, setDate] = useState(todayIso());
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');

  // Transaction-specific
  const [accountId, setAccountId] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);

  // Transfer-specific
  const [sourceAccountId, setSourceAccountId] = useState<string | null>(null);
  const [destAccountId, setDestAccountId] = useState<string | null>(null);

  // Liability Payment-specific
  const [assetAccountId, setAssetAccountId] = useState<string | null>(null);
  const [liabilityAccountId, setLiabilityAccountId] = useState<string | null>(null);

  // Load comboboxes when the modal opens
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    Promise.all([
      fetch(ACCOUNTS_ACTIVE_URL, { signal: controller.signal }).then((r) => r.json()),
      fetch(CATEGORIES_ACTIVE_URL, { signal: controller.signal }).then((r) => r.json()),
    ])
      .then(([accs, cats]) => {
        setAccounts(accs);
        setCategories(cats);
      })
      .catch((e) => {
        if (controller.signal.aborted) return;
        toast.error('Could not load accounts or categories.');
        // eslint-disable-next-line no-console
        console.error(e);
      });
    return () => controller.abort();
  }, [open]);

  function resetForm() {
    setDate(todayIso());
    setAmount('');
    setDescription('');
    setAccountId(null);
    setCategoryId(null);
    setSourceAccountId(null);
    setDestAccountId(null);
    setAssetAccountId(null);
    setLiabilityAccountId(null);
    setErrors({});
  }

  function selectedAccountSymbol(): string {
    const id =
      tab === 'transaction'
        ? accountId
        : tab === 'transfer'
          ? sourceAccountId
          : assetAccountId;
    return accounts.find((a) => a.id === id)?.currencySymbol ?? '';
  }

  async function submit() {
    setSubmitting(true);
    setErrors({});
    try {
      const url =
        tab === 'transaction' ? TRANSACTIONS_CREATE_URL :
        tab === 'transfer' ? TRANSFERS_CREATE_URL :
        LIABILITY_PAYMENTS_CREATE_URL;

      const body =
        tab === 'transaction'
          ? { date, amount: Number(amount), accountId, categoryId, description: description || null }
          : tab === 'transfer'
            ? { date, amount: Number(amount), sourceAccountId, destAccountId, description: description || null }
            : { date, amount: Number(amount), assetAccountId, liabilityAccountId, description: description || null };

      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (response.ok) {
        toast.success('Saved.');
        resetForm();
        onOpenChange(false);
        onSaved?.();
        return;
      }

      if (response.status === 422) {
        const problem = await response.json();
        const flat: FieldErrors = {};
        if (problem?.errors && typeof problem.errors === 'object') {
          for (const [key, messages] of Object.entries(problem.errors as Record<string, string[]>)) {
            // ASP.NET sends PascalCase keys; lowercase the first letter for matching
            const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
            flat[camelKey] = messages.join(' ');
          }
        }
        setErrors(flat);
        return;
      }

      toast.error("Couldn't save. Try again.");
    } catch {
      toast.error("Couldn't save. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Quick add</DialogTitle>
        </DialogHeader>

        <Tabs value={tab} onValueChange={(v) => { setTab(v as TabKey); setErrors({}); }}>
          <TabsList className="grid w-full grid-cols-3">
            <TabsTrigger value="transaction">Transaction</TabsTrigger>
            <TabsTrigger value="transfer">Transfer</TabsTrigger>
            <TabsTrigger value="liabilityPayment">Liability Payment</TabsTrigger>
          </TabsList>

          <TabsContent value="transaction" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-date" error={errors.date}>
              <Input id="qa-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Amount" htmlFor="qa-amount" error={errors.amount}>
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground w-6">{selectedAccountSymbol()}</span>
                <Input id="qa-amount" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
              </div>
            </Field>
            <Field label="Account" error={errors.accountId}>
              <AccountCombobox accounts={accounts} value={accountId} onChange={setAccountId} placeholder="Select account" />
            </Field>
            <Field label="Category" error={errors.categoryId}>
              <CategoryCombobox categories={categories} value={categoryId} onChange={setCategoryId} placeholder="Select category" />
            </Field>
            <Field label="Description" htmlFor="qa-desc" error={errors.description}>
              <Input id="qa-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>

          <TabsContent value="transfer" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-tr-date" error={errors.date}>
              <Input id="qa-tr-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Amount" htmlFor="qa-tr-amount" error={errors.amount}>
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground w-6">{selectedAccountSymbol()}</span>
                <Input id="qa-tr-amount" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
              </div>
            </Field>
            <Field label="Source account" error={errors.sourceAccountId}>
              <AccountCombobox accounts={accounts} value={sourceAccountId} onChange={setSourceAccountId} placeholder="Select source" />
            </Field>
            <Field label="Destination account" error={errors.destAccountId}>
              <AccountCombobox accounts={accounts} value={destAccountId} onChange={setDestAccountId} placeholder="Select destination" />
            </Field>
            <Field label="Description" htmlFor="qa-tr-desc" error={errors.description}>
              <Input id="qa-tr-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>

          <TabsContent value="liabilityPayment" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-lp-date" error={errors.date}>
              <Input id="qa-lp-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </Field>
            <Field label="Amount" htmlFor="qa-lp-amount" error={errors.amount}>
              <div className="flex items-center gap-2">
                <span className="text-sm text-muted-foreground w-6">{selectedAccountSymbol()}</span>
                <Input id="qa-lp-amount" type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} />
              </div>
            </Field>
            <Field label="Asset account" error={errors.assetAccountId}>
              <AccountCombobox
                accounts={accounts}
                value={assetAccountId}
                onChange={setAssetAccountId}
                placeholder="Select asset account"
                filter={(a) => a.accountTypeName !== 'Liability'}
              />
            </Field>
            <Field label="Liability account" error={errors.liabilityAccountId}>
              <AccountCombobox
                accounts={accounts}
                value={liabilityAccountId}
                onChange={setLiabilityAccountId}
                placeholder="Select liability account"
                filter={(a) => a.accountTypeName === 'Liability'}
              />
            </Field>
            <Field label="Description" htmlFor="qa-lp-desc" error={errors.description}>
              <Input id="qa-lp-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>
        </Tabs>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button onClick={submit} disabled={submitting}>{submitting ? 'Saving…' : 'Save'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label htmlFor={htmlFor}>{label}</Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/components/QuickAddModal.test.tsx`
Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/components/QuickAddModal.tsx ProjectCeres.Client/src/app/components/QuickAddModal.test.tsx ProjectCeres.Client/src/components/ui/tabs.tsx ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml
git commit -m "feat(quickadd): add QuickAddModal with 3 tabs and inline errors"
```

(`tabs.tsx` may already exist; `git add` no-ops silently if so.)

---

### Task 15: Build `MovementClearedToggle`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx`

This is a SPA-native version that PATCHes directly. The existing `src/components/IsClearedSwitch.tsx` is a Razor form input — different contract — and stays untouched.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementClearedToggle } from './MovementClearedToggle';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('MovementClearedToggle', () => {
  it('renders Cleared label when isCleared=true', () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={true} />);
    expect(screen.getByText('Cleared')).toBeInTheDocument();
  });

  it('renders Pending label when isCleared=false', () => {
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);
    expect(screen.getByText('Pending')).toBeInTheDocument();
  });

  it('PATCHes the API on click and updates the label', async () => {
    mockFetch.mockResolvedValue({ ok: true });
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('/api/movements/m1/cleared', expect.objectContaining({ method: 'PATCH' }));
      expect(screen.getByText('Cleared')).toBeInTheDocument();
    });
  });

  it('reverts the label and fires error toast on failed PATCH', async () => {
    const { toast } = await import('sonner');
    mockFetch.mockResolvedValue({ ok: false });
    render(<MovementClearedToggle id="m1" type="Transaction" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      expect(screen.getByText('Pending')).toBeInTheDocument();
      expect(toast.error).toHaveBeenCalledWith("Couldn't update status.");
    });
  });

  it('sends type=liabilitypayment for LiabilityPayment movements', async () => {
    mockFetch.mockResolvedValue({ ok: true });
    render(<MovementClearedToggle id="lp1" type="LiabilityPayment" isCleared={false} />);

    fireEvent.click(screen.getByRole('button'));

    await waitFor(() => {
      const call = mockFetch.mock.calls[0];
      const body = JSON.parse(call[1].body);
      expect(body.type).toBe('liabilitypayment');
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementClearedToggle.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `MovementClearedToggle`**

Create `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx`:

```tsx
import { CheckCircle, Clock } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import type { MovementType } from './movements-api';
import { MOVEMENTS_CLEARED_URL } from './movements-api';

type Props = {
  id: string;
  type: MovementType;
  isCleared: boolean;
};

function typeForApi(type: MovementType): string {
  if (type === 'Transaction') return 'transaction';
  if (type === 'Transfer') return 'transfer';
  return 'liabilitypayment';
}

export function MovementClearedToggle({ id, type, isCleared: initial }: Props) {
  const [cleared, setCleared] = useState(initial);

  async function toggle() {
    const next = !cleared;
    setCleared(next);

    try {
      const response = await fetch(MOVEMENTS_CLEARED_URL(id), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: typeForApi(type), cleared: next }),
      });
      if (!response.ok) {
        setCleared(!next);
        toast.error("Couldn't update status.");
      }
    } catch {
      setCleared(!next);
      toast.error("Couldn't update status.");
    }
  }

  return (
    <button
      type="button"
      onClick={toggle}
      aria-label={cleared ? 'Mark as pending' : 'Mark as cleared'}
      className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {cleared ? (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-success/10 text-success">
          <CheckCircle size={12} aria-hidden="true" />
          Cleared
        </span>
      ) : (
        <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-warning/10 text-warning">
          <Clock size={12} aria-hidden="true" />
          Pending
        </span>
      )}
    </button>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementClearedToggle.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.test.tsx
git commit -m "feat(movements): add MovementClearedToggle SPA component"
```

---

### Task 16: Build `MovementsTable`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx`

- [ ] **Step 1: Write failing test**

Create `ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MovementsTable } from './MovementsTable';
import type { MovementListItemDto } from './movements-api';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const items: MovementListItemDto[] = [
  {
    id: 't1', movementType: 'Transaction', date: '2026-04-15', amount: 25.5,
    currencyCode: 'EUR', currencySymbol: '€', description: 'Rent', isCleared: true,
    accountName: 'Checking', categoryName: 'Housing', categoryTypeName: 'Expense',
    sourceAccountName: null, destAccountName: null, assetAccountName: null, liabilityAccountName: null,
  },
  {
    id: 't2', movementType: 'Transfer', date: '2026-04-14', amount: 100,
    currencyCode: 'EUR', currencySymbol: '€', description: 'Move savings', isCleared: false,
    accountName: null, categoryName: null, categoryTypeName: null,
    sourceAccountName: 'Checking', destAccountName: 'Savings', assetAccountName: null, liabilityAccountName: null,
  },
  {
    id: 'lp1', movementType: 'LiabilityPayment', date: '2026-04-13', amount: 50,
    currencyCode: 'EUR', currencySymbol: '€', description: null, isCleared: false,
    accountName: null, categoryName: null, categoryTypeName: null,
    sourceAccountName: null, destAccountName: null, assetAccountName: 'Checking', liabilityAccountName: 'Credit Card',
  },
];

describe('MovementsTable', () => {
  it('renders all rows with badges', () => {
    render(<MovementsTable items={items} />);
    expect(screen.getByText('Transaction')).toBeInTheDocument();
    expect(screen.getByText('Transfer')).toBeInTheDocument();
    expect(screen.getByText('Liability Payment')).toBeInTheDocument();
    expect(screen.getByText('Rent')).toBeInTheDocument();
    expect(screen.getByText('Move savings')).toBeInTheDocument();
  });

  it('renders transfer arrow between source and destination', () => {
    render(<MovementsTable items={items} />);
    expect(screen.getByText(/Checking.*→.*Savings/)).toBeInTheDocument();
  });

  it('renders liability payment arrow between asset and liability', () => {
    render(<MovementsTable items={items} />);
    expect(screen.getByText(/Checking.*→.*Credit Card/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsTable.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `MovementsTable`**

Create `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx`:

```tsx
import { Numeric } from '@/components/Numeric';
import { cn } from '@/lib/utils';
import { MovementClearedToggle } from './MovementClearedToggle';
import type { MovementListItemDto, MovementType } from './movements-api';

type Props = { items: MovementListItemDto[] };

const typeBadgeClass: Record<MovementType, string> = {
  Transaction: 'bg-info/10 text-info',
  Transfer: 'bg-chart-4/10 text-chart-4',
  LiabilityPayment: 'bg-warning/10 text-warning',
};

const typeLabel: Record<MovementType, string> = {
  Transaction: 'Transaction',
  Transfer: 'Transfer',
  LiabilityPayment: 'Liability Payment',
};

function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString();
}

function amountColor(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') {
    if (item.categoryTypeName === 'Income') return 'text-success';
    if (item.categoryTypeName === 'Expense') return 'text-destructive';
  }
  return 'text-foreground';
}

export function MovementsTable({ items }: Props) {
  return (
    <div className="rounded-md border border-border overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40">
          <tr>
            <th className="text-left px-3 py-2 font-medium">Date</th>
            <th className="text-left px-3 py-2 font-medium">Type</th>
            <th className="text-left px-3 py-2 font-medium">Account(s)</th>
            <th className="text-left px-3 py-2 font-medium">Category / Details</th>
            <th className="text-left px-3 py-2 font-medium">Description</th>
            <th className="text-right px-3 py-2 font-medium">Amount</th>
            <th className="text-left px-3 py-2 font-medium">Status</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={`${item.movementType}-${item.id}`} className="border-t border-border">
              <td className="px-3 py-2 whitespace-nowrap">{formatDate(item.date)}</td>
              <td className="px-3 py-2">
                <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', typeBadgeClass[item.movementType])}>
                  {typeLabel[item.movementType]}
                </span>
              </td>
              <td className="px-3 py-2">
                {item.movementType === 'Transaction' && item.accountName}
                {item.movementType === 'Transfer' && `${item.sourceAccountName} → ${item.destAccountName}`}
                {item.movementType === 'LiabilityPayment' && `${item.assetAccountName} → ${item.liabilityAccountName}`}
              </td>
              <td className="px-3 py-2">
                {item.movementType === 'Transaction' ? item.categoryName : '—'}
              </td>
              <td className="px-3 py-2">{item.description ?? '—'}</td>
              <td className="px-3 py-2 text-right whitespace-nowrap">
                <Numeric className={amountColor(item)}>
                  {item.currencySymbol} {item.amount.toFixed(2)}
                </Numeric>
              </td>
              <td className="px-3 py-2">
                <MovementClearedToggle id={item.id} type={item.movementType} isCleared={item.isCleared} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsTable.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx ProjectCeres.Client/src/app/features/movements/MovementsTable.test.tsx
git commit -m "feat(movements): add MovementsTable component"
```

---

### Task 17: Build `MovementsFilterBar`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx`

- [ ] **Step 1: Write failing test**

Create `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsFilterBar } from './MovementsFilterBar';

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    json: async () => [
      { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    ],
  });
});

afterEach(() => { vi.resetAllMocks(); });

function LocationSpy({ onChange }: { onChange: (search: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderBar(initial = '/movements') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsFilterBar />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsFilterBar', () => {
  it('renders the search input and date inputs', () => {
    renderBar();
    expect(screen.getByPlaceholderText(/search description or category/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/from/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to/i)).toBeInTheDocument();
  });

  it('updates the URL when the from date changes', async () => {
    const getSearch = renderBar();
    fireEvent.change(screen.getByLabelText(/from/i), { target: { value: '2026-04-01' } });
    await waitFor(() => {
      expect(getSearch()).toContain('from=2026-04-01');
    });
  });

  it('clears all params when "Clear" is clicked', async () => {
    const getSearch = renderBar('/movements?q=hi&from=2026-04-01');
    fireEvent.click(screen.getByRole('button', { name: /clear/i }));
    await waitFor(() => {
      expect(getSearch()).toBe('');
    });
  });
});
```

- [ ] **Step 2: Run test to verify failure**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsFilterBar.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `MovementsFilterBar`**

Create `ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useApi } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from './movements-api';
import { AccountCombobox } from '../../components/AccountCombobox';

export function MovementsFilterBar() {
  const [params, setParams] = useSearchParams();
  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);

  // Local search input state — debounced before pushing to URL
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');
  const debouncedSearch = useDebounced(searchInput, 300);

  useEffect(() => {
    const current = params.get('q') ?? '';
    if (debouncedSearch === current) return;
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    next.delete('page'); // reset to first page on filter change
    setParams(next, { replace: true });
  }, [debouncedSearch]); // eslint-disable-line react-hooks/exhaustive-deps

  function setParam(key: string, value: string | null) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    next.delete('page');
    setParams(next, { replace: true });
  }

  const hasFilters = ['q', 'accountId', 'from', 'to'].some((k) => params.get(k));

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
      <div className="flex-1 space-y-1.5">
        <Label htmlFor="mov-search">Search</Label>
        <Input
          id="mov-search"
          placeholder="Search description or category…"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </div>
      <div className="space-y-1.5 sm:w-56">
        <Label>Account</Label>
        <AccountCombobox
          accounts={accounts ?? []}
          value={params.get('accountId')}
          onChange={(id) => setParam('accountId', id)}
          placeholder="All accounts"
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="mov-from">From</Label>
        <Input
          id="mov-from"
          type="date"
          value={params.get('from') ?? ''}
          onChange={(e) => setParam('from', e.target.value || null)}
        />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="mov-to">To</Label>
        <Input
          id="mov-to"
          type="date"
          value={params.get('to') ?? ''}
          onChange={(e) => setParam('to', e.target.value || null)}
        />
      </div>
      {hasFilters && (
        <Button
          variant="outline"
          onClick={() => {
            setSearchInput('');
            setParams(new URLSearchParams(), { replace: true });
          }}
        >
          Clear
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsFilterBar.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.tsx ProjectCeres.Client/src/app/features/movements/MovementsFilterBar.test.tsx
git commit -m "feat(movements): add MovementsFilterBar with debounced search"
```

---

### Task 18: Build `MovementsPagination`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsPagination.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsPagination.test.tsx`

- [ ] **Step 1: Write failing test**

Create `ProjectCeres.Client/src/app/features/movements/MovementsPagination.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { MovementsPagination } from './MovementsPagination';

function LocationSpy({ onChange }: { onChange: (s: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderPagination(props: { totalCount: number; pageSize: number }, initial = '/movements') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsPagination {...props} />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsPagination', () => {
  it('renders nothing when totalCount fits in one page', () => {
    renderPagination({ totalCount: 30, pageSize: 50 });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders page links when totalCount exceeds one page', () => {
    renderPagination({ totalCount: 120, pageSize: 50 });
    expect(screen.getByRole('button', { name: '1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '3' })).toBeInTheDocument();
  });

  it('updates ?page= when a page is clicked', async () => {
    const getSearch = renderPagination({ totalCount: 120, pageSize: 50 });
    fireEvent.click(screen.getByRole('button', { name: '2' }));
    await waitFor(() => {
      expect(getSearch()).toContain('page=2');
    });
  });

  it('marks the current page as active', () => {
    renderPagination({ totalCount: 120, pageSize: 50 }, '/movements?page=2');
    const page2 = screen.getByRole('button', { name: '2' });
    expect(page2).toHaveAttribute('aria-current', 'page');
  });
});
```

- [ ] **Step 2: Run test to verify failure**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsPagination.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `MovementsPagination`**

Create `ProjectCeres.Client/src/app/features/movements/MovementsPagination.tsx`:

```tsx
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';

type Props = {
  totalCount: number;
  pageSize: number;
};

export function MovementsPagination({ totalCount, pageSize }: Props) {
  const [params, setParams] = useSearchParams();
  const totalPages = Math.ceil(totalCount / pageSize);
  if (totalPages <= 1) return null;

  const currentPage = Number(params.get('page') ?? '1');

  function goToPage(page: number) {
    const next = new URLSearchParams(params);
    if (page === 1) next.delete('page');
    else next.set('page', String(page));
    setParams(next, { replace: true });
  }

  return (
    <nav aria-label="Pagination" className="flex items-center justify-center gap-1">
      {Array.from({ length: totalPages }, (_, i) => i + 1).map((page) => (
        <Button
          key={page}
          variant={page === currentPage ? 'default' : 'outline'}
          size="sm"
          aria-current={page === currentPage ? 'page' : undefined}
          onClick={() => goToPage(page)}
        >
          {page}
        </Button>
      ))}
    </nav>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/movements/MovementsPagination.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsPagination.tsx ProjectCeres.Client/src/app/features/movements/MovementsPagination.test.tsx
git commit -m "feat(movements): add MovementsPagination component"
```

---

### Task 19: Wire `Movements` page + TopBar quick-add

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Movements.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/TopBar.tsx`

- [ ] **Step 1: Replace the placeholder `Movements.tsx`**

Replace the entire contents of `ProjectCeres.Client/src/app/pages/Movements.tsx` with:

```tsx
import { Plus } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../features/dashboard/CardError';
import { MovementsFilterBar } from '../features/movements/MovementsFilterBar';
import { MovementsPagination } from '../features/movements/MovementsPagination';
import { MovementsTable } from '../features/movements/MovementsTable';
import { MOVEMENTS_URL, type MovementsPageDto } from '../features/movements/movements-api';
import { QuickAddModal } from '../components/QuickAddModal';
import { useApi } from '../lib/use-api';

function buildUrl(params: URLSearchParams): string {
  const search = params.toString();
  return search ? `${MOVEMENTS_URL}?${search}` : MOVEMENTS_URL;
}

export function Movements() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [params] = useSearchParams();
  const url = buildUrl(params);
  const { data, error, loading, refetch } = useApi<MovementsPageDto>(url);

  const [quickAddOpen, setQuickAddOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 ref={headingRef} tabIndex={-1} className="text-3xl font-semibold outline-none">
          Movements
        </h1>
        <Button onClick={() => setQuickAddOpen(true)}>
          <Plus className="h-4 w-4" />
          New
        </Button>
      </div>

      <MovementsFilterBar />

      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Movements" onRetry={refetch} />}
      {data && data.items.length === 0 && (
        <p className="text-sm text-muted-foreground">No movements found.</p>
      )}
      {data && data.items.length > 0 && (
        <>
          <MovementsTable items={data.items} />
          <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
        </>
      )}

      <QuickAddModal
        open={quickAddOpen}
        onOpenChange={setQuickAddOpen}
        onSaved={refetch}
      />
    </div>
  );
}
```

- [ ] **Step 2: Update `TopBar.tsx` to use the real `QuickAddModal`**

Open `ProjectCeres.Client/src/app/layout/TopBar.tsx`. Replace the imports of `Dialog`, `DialogContent`, `DialogHeader`, `DialogTitle` with an import of `QuickAddModal`, and replace the `<Dialog>...</Dialog>` block with:

```tsx
import { QuickAddModal } from '../components/QuickAddModal';
```

(Remove the now-unused `Dialog` imports.)

Replace the `<Dialog open={quickAddOpen}...>...</Dialog>` block at the bottom of the file with:

```tsx
<QuickAddModal open={quickAddOpen} onOpenChange={setQuickAddOpen} />
```

(No `onSaved` from TopBar — cross-page refetch is deferred per spec §9.)

- [ ] **Step 3: Type-check + run all client tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No TS errors. All tests pass.

- [ ] **Step 4: Manual smoke test**

DO NOT execute this — the user runs it.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/Movements.tsx ProjectCeres.Client/src/app/layout/TopBar.tsx
git commit -m "feat(movements): wire Movements page and TopBar QuickAddModal"
```

---

### Task 20: Razor cleanup (302 redirect + delete view)

**Files:**
- Modify: `ProjectCeres/Controllers/MovementsController.cs`
- Modify: `ProjectCeres.Tests/Integration/MovementsControllerTests.cs`
- Delete: `ProjectCeres/Views/Movements/Index.cshtml`

- [ ] **Step 1: Update the existing controller test for the redirect**

Open `ProjectCeres.Tests/Integration/MovementsControllerTests.cs`. Find the existing test that asserts `GET /Movements` returns 200 OK. Replace it with a 302 redirect assertion:

```csharp
[Fact]
public async Task GetMovementsRoot_Returns302_RedirectingToAppShell()
{
    using var noRedirectClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    var response = await noRedirectClient.GetAsync("/Movements");

    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location.Should().NotBeNull();
    response.Headers.Location!.ToString().Should().Be("/app/movements");
}
```

(Other assertions in this test file about returnUrl behavior on Transactions/Transfers should remain — they test those Razor pages which we're NOT touching this slice.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetMovementsRoot_Returns302"`
Expected: FAIL — current `MovementsController.Index()` returns the Razor view (200 OK).

- [ ] **Step 3: Replace `MovementsController.cs` with a redirect-only stub**

Replace the contents of `ProjectCeres/Controllers/MovementsController.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class MovementsController : Controller
{
    public IActionResult Index() => Redirect("/app/movements");
}
```

- [ ] **Step 4: Delete the Razor view**

Run:

```bash
rm ProjectCeres/Views/Movements/Index.cshtml
rmdir ProjectCeres/Views/Movements
```

- [ ] **Step 5: Run all backend + frontend tests**

Run: `dotnet test ProjectCeres.Tests && cd ProjectCeres.Client && pnpm test`
Expected: All pass.

- [ ] **Step 6: Manual smoke test**

DO NOT execute. The user runs it.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Controllers/MovementsController.cs ProjectCeres/Views/Movements ProjectCeres.Tests/Integration/MovementsControllerTests.cs
git commit -m "chore(movements): retire Razor Movements page, redirect to SPA"
```

---

### Task 21: Documentation updates

**Files:**
- Modify: `docs/api-contract.md`
- Modify: `docs/planning-phase3-spa-migration.md`
- Modify: `docs/planning-phase3.md`
- Modify: `docs/design-system.md`

- [ ] **Step 1: Update `docs/api-contract.md`**

Read the file first to match its style. Add rows for:

- `GET /api/movements` → `MovementsPageDto { items: MovementListItemDto[], totalCount, page, pageSize }`. Query params: `q`, `accountId`, `from`, `to`, `page`, `pageSize`.
- `POST /api/transactions` → request body `CreateTransactionRequest { date, amount, accountId, categoryId, description }`. Returns `201 Created` with `{ id }` body. `422 Unprocessable Entity` with `ValidationProblemDetails` on invalid input.
- `POST /api/transfers` → request body `CreateTransferRequest { date, amount, sourceAccountId, destAccountId, description }`. Returns `201` with `{ id }`. `422` if source == destination, on cross-currency, or other validation failures.
- `POST /api/liability-payments` → request body `CreateLiabilityPaymentRequest { date, amount, assetAccountId, liabilityAccountId, description }`. Returns `201` with `{ id }`. `422` on validation failures.
- `GET /api/accounts/active` → `AccountOptionDto[] { id, name, currencyCode, currencySymbol, accountTypeName }`. Active accounts only.
- `GET /api/categories/active` → `CategoryOptionDto[] { id, name, categoryTypeName }`. Active, non-system categories only.

Match field names exactly with `ProjectCeres/ViewModels/MovementsApiDtos.cs`, `QuickAddDtos.cs`, `ComboboxOptionDtos.cs`.

- [ ] **Step 2: Update `docs/planning-phase3-spa-migration.md`**

In the controller porting map (§2), update the row for `MovementsController` to read **Migrated (2026-04-30)**, with a note that the 302 redirect from `/Movements` → `/app/movements` is live and `Views/Movements/Index.cshtml` is deleted.

For `TransactionsController`, `TransfersController`, and the (implicit) liability-payments flow: add a note that **Create** is partially served via new API endpoints (`POST /api/transactions`, `/api/transfers`, `/api/liability-payments`) for the SPA quick-add modal, but the full Razor CRUD (Index/Edit/Delete) still serves the Razor pages until that slice migrates.

- [ ] **Step 3: Update `docs/planning-phase3.md`**

Find item 6 (Movements + Transactions + Transfers) in the Implementation Order section. Mark Movements + quick-add chunk as complete with a sub-bullet:

```
6. Movements + Transactions + Transfers — highest daily usage; includes quick-add, per-table search, saved searches
   - ✓ **Movements list page + quick-add (2026-04-30).** SPA Movements at `/app/movements` with text search + 3 filters. Quick-add modal wired to TopBar `+` and Movements page header. POST endpoints for Transactions, Transfers, Liability Payments. Sonner toasts. Razor `MovementsController` redirected to SPA.
   - Pending: Per-table search & saved searches (own brainstorm).
   - Pending: Full Transactions/Transfers/LiabilityPayments CRUD (Index/Edit/Delete).
```

- [ ] **Step 4: Update `docs/design-system.md`**

Read the file first to match its style. Add a new section about Sonner usage:

```markdown
## Toasts

The SPA uses [Sonner](https://sonner.emilkowal.ski/) for toast notifications. A single `<Toaster />` is mounted in `AppLayout.tsx` — anywhere in the app, import `toast` from `sonner` and call:

- `toast.success('Saved.')` for successful operations
- `toast.error("Couldn't update status.")` for failures

Recommended message style: short, sentence-case, ends in a period. Past tense for completed actions ("Saved."), contraction-friendly for failures ("Couldn't save.").

For destructive operations, prefer a confirmation dialog over a toast.
```

- [ ] **Step 5: Commit**

```bash
git add docs/api-contract.md docs/planning-phase3-spa-migration.md docs/planning-phase3.md docs/design-system.md
git commit -m "docs(movements): record Movements + quick-add outcomes"
```

---

## Self-Review Notes

**Spec coverage check:**
- §1 Goal — covered.
- §2 Architecture — Tasks 1–8 (backend), Tasks 9–18 (frontend), Tasks 19 (wiring), 20 (Razor cleanup), 21 (docs).
- §3a DTOs — Tasks 2, 5, 8.
- §3b API controllers — Tasks 3, 6, 7, 8.
- §3c service `q` parameter — Task 1.
- §3d frontend feature folder — Task 11 (`movements-api.ts`), Task 16 (`MovementsTable`), Task 17 (`MovementsFilterBar`), Task 18 (`MovementsPagination`), Task 15 (`MovementClearedToggle`).
- §3e Movements page — Task 19.
- §3f QuickAddModal — Task 14.
- §3g currency on amount — Task 14 (`selectedAccountSymbol()`).
- §3h combobox helper endpoints — Task 8.
- §3i TopBar wiring — Task 19.
- §3j page wiring (`onSaved={refetch}`) — Task 19.
- §3k AppLayout `<Toaster />` — Task 9.
- §4 Layout — Task 19 (page), Task 17 (filter bar), Task 16 (table), Task 14 (modal).
- §5 Razor cleanup — Task 20.
- §6 Data flow — covered by component implementations (Task 14: 422 → ModelState mapping; Task 19: `useApi` URL re-fetch; Task 15: optimistic toggle + revert).
- §7 Testing — every component/endpoint task has tests; Razor 302 in Task 20.
- §8 Documentation — Task 21.
- §9 Out-of-scope items — none implemented; all noted as pending in Task 21 docs updates.

**Type consistency check:**
- `MovementType` enum/union: backend uses `MovementType.Transaction|Transfer|LiabilityPayment` (existing enum). API controller serializes it via `.ToString()` → `"Transaction"|"Transfer"|"LiabilityPayment"`. Frontend type matches: `'Transaction' | 'Transfer' | 'LiabilityPayment'`.
- PATCH cleared `type` field accepts lowercase strings — `MovementClearedToggle.typeForApi()` lowercases before sending.
- Combobox options: `accountTypeName` matches `"Asset"` or `"Liability"` (per `AccountType` seed data in `AppDbContext.cs`). Filter logic in QuickAddModal LP tab uses `accountTypeName !== 'Liability'` for asset side and `accountTypeName === 'Liability'` for liability side — consistent.
- `ServerValidationProblem.errors` matches ASP.NET's `ValidationProblemDetails.Errors` shape (PascalCase keys, string-array values). `QuickAddModal` lowercases the first letter to match TS prop names.

**Placeholder scan:** None. Every code step has complete code; every test has complete assertions; every commit has a complete message.

**Path consistency:** Frontend paths use `'../../lib/use-api'`, `'../../lib/use-debounced'`, `'../components/AccountCombobox'`, etc. — matches the existing convention used by `Movements.tsx` placeholder and other pages.
