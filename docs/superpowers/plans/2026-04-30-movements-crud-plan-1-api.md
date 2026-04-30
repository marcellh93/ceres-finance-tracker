# Movements CRUD — Plan 1: API surface (server-only)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the full server-side API surface for Movements CRUD: typed `GET / PUT / DELETE` for Transactions, Transfers, Liability Payments; the type filter on `GET /api/movements`; a discriminator endpoint at `GET /api/movements/{id}`; `POST /api/movements/bulk-cleared`; `GET /api/movements/export.csv`; and attachment endpoints for Transactions and Transfers. The SPA still uses old paths after this plan — the contract is in place but unused.

**Architecture:** Each resource gets new actions on its existing `Api` controller. Service-layer methods (`GetByIdForEditAsync`, `UpdateAsync`, `DeleteAsync`, `BulkMarkClearedAsync`) already exist for Transactions; Transfers and Liability Payments need only thin additions where missing. New typed *Edit DTOs* and *Update Request* shapes mirror the existing `*EditViewModel` field set, JSON-shaped. Validation follows the established `[ApiController]` + `InvalidModelStateResponseFactory` → 422 pattern; cross-field errors return 422 explicitly with the standard `error.details[]` body. Attachments use `multipart/form-data` posted to `POST /api/transactions/{id}/attachments` and the Transfer equivalent, delegating to the existing `IFileAttachmentService`.

**Tech Stack:** ASP.NET Core 10 Web API, EF Core (Npgsql), xUnit + FluentAssertions integration tests against `TestWebApplicationFactory`.

**Spec:** `docs/superpowers/specs/2026-04-30-movements-crud.md`. Read §5 (API surface) before starting any task.

**Scope boundary:** Server-only. No client changes. No Razor changes. The Razor controllers remain in place and continue to serve the existing UI. After this plan, all 14 endpoints exist with passing integration tests; SPA still hits only the existing endpoints.

---

## File Structure

**Created:**
- `ProjectCeres/ViewModels/MovementCrudDtos.cs` — JSON request/response DTOs for the new endpoints (see Task 2 for full contents).
- `ProjectCeres/Services/MovementExportService.cs` + `IMovementExportService.cs` — builds the CSV body for the Movements export.
- `ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/TransfersCrudApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/LiabilityPaymentsCrudApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/MovementsTypeFilterTests.cs`
- `ProjectCeres.Tests/Integration/Api/MovementsDiscriminatorTests.cs`
- `ProjectCeres.Tests/Integration/Api/MovementsBulkClearedTests.cs`
- `ProjectCeres.Tests/Integration/Api/MovementsExportTests.cs`
- `ProjectCeres.Tests/Integration/Api/TransactionAttachmentsApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/TransferAttachmentsApiTests.cs`

**Modified:**
- `ProjectCeres/Services/IMovementService.cs` — extends `GetRecentAsync` and `CountAsync` with an optional `MovementType?` filter parameter.
- `ProjectCeres/Services/MovementService.cs` — applies the type filter by skipping the queries we don't need; adds `GetTypeAsync(Guid)` for the discriminator.
- `ProjectCeres/Services/ILiabilityPaymentService.cs` — adds `Task BulkMarkClearedAsync(...)` (Transaction service already has it; Transfer service does too — verify in Task 14).
- `ProjectCeres/Controllers/Api/TransactionsApiController.cs` — adds `GET /:id`, `PUT /:id`, `DELETE /:id`, `POST /:id/attachments`, `DELETE /attachments/:attachmentId`.
- `ProjectCeres/Controllers/Api/TransfersApiController.cs` — same shape.
- `ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs` — `GET /:id`, `PUT /:id`, `DELETE /:id` (no attachments per spec non-goals).
- `ProjectCeres/Controllers/Api/MovementsApiController.cs` — extends `GetMovements` with `type` query param; adds `GET /:id` discriminator, `POST /bulk-cleared`, `GET /export.csv`.
- `ProjectCeres/Program.cs` — registers `IMovementExportService` (Task 26).

---

## Conventions used throughout this plan

- **Test collection:** every new test class is annotated `[Collection("IntegrationTests")]` and implements `IAsyncLifetime` with `DisposeAsync` cleaning up seeded rows by id. Match the pattern in `ProjectCeres.Tests/Integration/MovementsApiTests.cs`.
- **Seeded ids:** generated via `Guid.NewGuid()` per test; tracked in private lists; cleaned in `DisposeAsync` via `ExecuteDeleteAsync`.
- **System category id reused** for Transactions: `20000000-0000-0000-0000-000000000008` (Housing) — already used by `TransactionsApiTests.cs`.
- **System currency id:** `1` (the default seeded currency), per existing tests.
- **Account type ids:** `1` = Asset, `2` = Liability, per the seed migration. Verify with `grep` if a Task fails.
- **Each task ends with one commit.** No "amend" — if a hook fails, fix and create a new commit.

---

## Task 1: Verify the working tree is clean and on `main`

**Files:** none (sanity check).

- [ ] **Step 1: Confirm current branch and clean tree**

```bash
git status && git log --oneline -3
```

Expected: `working tree clean`, on branch `main`, last commit is `63a7e79 docs(spec): movements CRUD …` (or later). If anything else is pending, stop and resolve before proceeding.

---

## Task 2: Add the JSON DTO file (no behaviour yet — types only)

**Files:**
- Create: `ProjectCeres/ViewModels/MovementCrudDtos.cs`

- [ ] **Step 1: Create the DTO file**

```csharp
// ProjectCeres/ViewModels/MovementCrudDtos.cs
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

// ---------- Read DTOs (responses for GET /:id) ----------

public record TransactionEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid AccountId,
    Guid CategoryId,
    string? Description,
    bool IsCleared,
    IReadOnlyList<AttachmentDto> Attachments);

public record TransferEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid SourceAccountId,
    Guid DestAccountId,
    string? Description,
    bool IsCleared,
    IReadOnlyList<AttachmentDto> Attachments);

public record LiabilityPaymentEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid AssetAccountId,
    Guid LiabilityAccountId,
    string? Description,
    bool IsCleared);

public record AttachmentDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    DateTime UploadedAt);

public record MovementTypeDto(Guid Id, string MovementType);

// ---------- Write DTOs (request bodies for PUT /:id) ----------

public class UpdateTransactionRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public Guid? CategoryId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }
}

public class UpdateTransferRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select a source account.")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Please select a destination account.")]
    public Guid? DestAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }
}

public class UpdateLiabilityPaymentRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an asset account.")]
    public Guid? AssetAccountId { get; set; }

    [Required(ErrorMessage = "Please select a liability account.")]
    public Guid? LiabilityAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }
}

// ---------- Bulk-cleared request ----------

public class BulkClearedRequest
{
    [Required] public DateOnly From { get; set; }
    [Required] public DateOnly To   { get; set; }
    public Guid?  AccountId { get; set; }
    /// <summary>"transaction" | "transfer" | "liabilitypayment" | null (all).</summary>
    public string? Type { get; set; }
}
```

- [ ] **Step 2: Build the solution to make sure DTOs compile**

```bash
dotnet build ProjectCeres
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/ViewModels/MovementCrudDtos.cs
git commit -m "feat(api): add Movement CRUD DTOs (TransactionEditDto, UpdateTransactionRequest, etc.)"
```

---

## Task 3: Failing test for `GET /api/transactions/{id}` → 200

**Files:**
- Create: `ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs`

- [ ] **Step 1: Write the failing test class with one test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransactionsCrudApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransactionsCrudApiTests(TestWebApplicationFactory factory)
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
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid AccountId, Guid TransactionId)> SeedTransactionAsync(decimal amount = 25.50m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"TxCrud-Acct-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);

        var tx = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = new DateOnly(2026, 4, 15),
            Amount      = amount,
            AccountId   = account.Id,
            CategoryId  = HousingCategoryId,
            Description = "Existing tx",
            IsCleared   = false
        };
        db.Transactions.Add(tx);
        _seededTransactionIds.Add(tx.Id);

        await db.SaveChangesAsync();
        return (account.Id, tx.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (accountId, txId) = await SeedTransactionAsync();

        var response = await _client.GetAsync($"/api/transactions/{txId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(txId);
        body.GetProperty("amount").GetDecimal().Should().Be(25.50m);
        body.GetProperty("accountId").GetGuid().Should().Be(accountId);
        body.GetProperty("categoryId").GetGuid().Should().Be(HousingCategoryId);
        body.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        body.GetProperty("attachments").EnumerateArray().Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test — expect FAIL**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests.Get_Returns200_WithEditDto"
```

Expected: FAIL with 404 (the route does not exist yet).

- [ ] **Step 3: Commit the failing test**

```bash
git add ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs
git commit -m "test(api): failing test for GET /api/transactions/:id"
```

---

## Task 4: Implement `GET /api/transactions/{id}` → 200 / 404

**Files:**
- Modify: `ProjectCeres/Controllers/Api/TransactionsApiController.cs`

- [ ] **Step 1: Add the GET action**

Open `ProjectCeres/Controllers/Api/TransactionsApiController.cs` and append inside the controller class:

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<TransactionEditDto>> Get(Guid id)
{
    var vm = await transactionService.GetByIdForEditAsync(id);
    if (vm is null) return NotFound();

    using var scope = HttpContext.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var attachments = await db.TransactionAttachments
        .Where(a => a.TransactionId == id)
        .Select(a => new AttachmentDto(a.Id, a.FileName, a.SizeBytes, a.ContentType, a.UploadedAt))
        .ToListAsync();

    return new TransactionEditDto(
        Id:          vm.Id,
        Date:        vm.Date,
        Amount:      vm.Amount,
        AccountId:   vm.AccountId!.Value,
        CategoryId:  vm.CategoryId!.Value,
        Description: vm.Description,
        IsCleared:   vm.IsCleared,
        Attachments: attachments);
}
```

Add the missing using statements at the top of the file:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
```

- [ ] **Step 2: Run the test — expect PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests.Get_Returns200_WithEditDto"
```

Expected: PASS.

- [ ] **Step 3: Add and run the 404 test**

Append to `TransactionsCrudApiTests`:

```csharp
[Fact]
public async Task Get_Returns404_WhenIdMissing()
{
    var response = await _client.GetAsync($"/api/transactions/{Guid.NewGuid()}");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests"
```

Expected: PASS for both tests.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransactionsApiController.cs ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs
git commit -m "feat(api): GET /api/transactions/:id returns TransactionEditDto"
```

---

## Task 5: Failing tests for `PUT /api/transactions/{id}`

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs`

- [ ] **Step 1: Append three tests — happy path, 422 on bad amount, 404 on missing id**

```csharp
[Fact]
public async Task Put_Returns204_AndUpdatesFields()
{
    var (accountId, txId) = await SeedTransactionAsync();

    var response = await _client.PutAsJsonAsync($"/api/transactions/{txId}", new
    {
        date        = "2026-04-20",
        amount      = 99.00m,
        accountId   = accountId,
        categoryId  = HousingCategoryId,
        description = "Updated",
        isCleared   = true
    });

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var saved = await db.Transactions.FindAsync(txId);
    saved!.Amount.Should().Be(99.00m);
    saved.Description.Should().Be("Updated");
    saved.IsCleared.Should().BeTrue();
}

[Fact]
public async Task Put_Returns422_WhenAmountZero()
{
    var (accountId, txId) = await SeedTransactionAsync();

    var response = await _client.PutAsJsonAsync($"/api/transactions/{txId}", new
    {
        date        = "2026-04-20",
        amount      = 0m,
        accountId   = accountId,
        categoryId  = HousingCategoryId,
        isCleared   = false
    });

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
}

[Fact]
public async Task Put_Returns404_WhenIdMissing()
{
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var account = new Account
    {
        Id = Guid.NewGuid(), Name = $"Tx-Put-Missing-{Guid.NewGuid():N}",
        AccountTypeId = 1, CurrencyId = 1, IsActive = true
    };
    db.Accounts.Add(account);
    _seededAccountIds.Add(account.Id);
    await db.SaveChangesAsync();

    var response = await _client.PutAsJsonAsync($"/api/transactions/{Guid.NewGuid()}", new
    {
        date        = "2026-04-20",
        amount      = 10m,
        accountId   = account.Id,
        categoryId  = HousingCategoryId,
        isCleared   = false
    });

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] **Step 2: Run — expect 3 FAILs (no PUT route yet)**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests.Put_"
```

Expected: 3 FAILs (likely 405 Method Not Allowed or 404).

- [ ] **Step 3: Commit failing tests**

```bash
git add ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs
git commit -m "test(api): failing tests for PUT /api/transactions/:id"
```

---

## Task 6: Implement `PUT /api/transactions/{id}`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/TransactionsApiController.cs`

- [ ] **Step 1: Add the PUT action**

Append inside the controller:

```csharp
[HttpPut("{id:guid}")]
public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransactionRequest request)
{
    // [ApiController] auto-runs ModelState → 422 via InvalidModelStateResponseFactory.

    var existing = await transactionService.GetByIdForEditAsync(id);
    if (existing is null) return NotFound();

    var vm = new TransactionEditViewModel
    {
        Id              = id,
        TransactionType = "Regular",
        Date            = request.Date,
        Amount          = request.Amount,
        AccountId       = request.AccountId,
        CategoryId      = request.CategoryId,
        Description     = request.Description,
        IsCleared       = request.IsCleared
    };

    await transactionService.UpdateAsync(vm);
    return NoContent();
}
```

- [ ] **Step 2: Run — expect 3 PASSes**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests.Put_"
```

Expected: PASS for all three.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransactionsApiController.cs
git commit -m "feat(api): PUT /api/transactions/:id updates a transaction"
```

---

## Task 7: Failing test + implementation for `DELETE /api/transactions/{id}`

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs`
- Modify: `ProjectCeres/Controllers/Api/TransactionsApiController.cs`

- [ ] **Step 1: Append two failing tests**

```csharp
[Fact]
public async Task Delete_Returns204_AndHardDeletes()
{
    var (_, txId) = await SeedTransactionAsync();

    var response = await _client.DeleteAsync($"/api/transactions/{txId}");
    response.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    (await db.Transactions.FindAsync(txId)).Should().BeNull();
    _seededTransactionIds.Remove(txId);
}

[Fact]
public async Task Delete_Returns404_WhenIdMissing()
{
    var response = await _client.DeleteAsync($"/api/transactions/{Guid.NewGuid()}");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] **Step 2: Run — expect FAILs**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests.Delete_"
```

- [ ] **Step 3: Add the action**

Append inside the controller:

```csharp
[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id)
{
    var existing = await transactionService.GetByIdForEditAsync(id);
    if (existing is null) return NotFound();
    await transactionService.DeleteAsync(id);
    return NoContent();
}
```

- [ ] **Step 4: Run — expect PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionsCrudApiTests"
```

Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/Api/TransactionsCrudApiTests.cs ProjectCeres/Controllers/Api/TransactionsApiController.cs
git commit -m "feat(api): DELETE /api/transactions/:id hard-deletes the transaction"
```

---

## Task 8: Failing tests for Transfers `GET /:id`, `PUT /:id`, `DELETE /:id`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Api/TransfersCrudApiTests.cs`

- [ ] **Step 1: Create the test class with seeding helper and 5 tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransfersCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransfersCrudApiTests(TestWebApplicationFactory factory)
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
            await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid SourceId, Guid DestId, Guid TransferId)> SeedTransferAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var src  = new Account { Id = Guid.NewGuid(), Name = $"Tr-Src-{Guid.NewGuid():N}",  AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var dest = new Account { Id = Guid.NewGuid(), Name = $"Tr-Dst-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(src, dest);
        _seededAccountIds.Add(src.Id);
        _seededAccountIds.Add(dest.Id);

        var tr = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = new DateOnly(2026, 4, 10),
            Amount          = 200m,
            SourceAccountId = src.Id,
            DestAccountId   = dest.Id,
            Description     = "Initial transfer",
            IsCleared       = false
        };
        db.Transfers.Add(tr);
        _seededTransferIds.Add(tr.Id);

        await db.SaveChangesAsync();
        return (src.Id, dest.Id, tr.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (srcId, destId, trId) = await SeedTransferAsync();

        var response = await _client.GetAsync($"/api/transfers/{trId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(trId);
        body.GetProperty("sourceAccountId").GetGuid().Should().Be(srcId);
        body.GetProperty("destAccountId").GetGuid().Should().Be(destId);
        body.GetProperty("amount").GetDecimal().Should().Be(200m);
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/transfers/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Returns204_AndUpdatesFields()
    {
        var (srcId, destId, trId) = await SeedTransferAsync();

        var response = await _client.PutAsJsonAsync($"/api/transfers/{trId}", new
        {
            date            = "2026-04-15",
            amount          = 350m,
            sourceAccountId = srcId,
            destAccountId   = destId,
            description     = "Updated",
            isCleared       = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Transfers.FindAsync(trId);
        saved!.Amount.Should().Be(350m);
        saved.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Put_Returns422_WhenSourceEqualsDest()
    {
        var (srcId, _, trId) = await SeedTransferAsync();

        var response = await _client.PutAsJsonAsync($"/api/transfers/{trId}", new
        {
            date            = "2026-04-15",
            amount          = 100m,
            sourceAccountId = srcId,
            destAccountId   = srcId,
            description     = (string?)null,
            isCleared       = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Delete_Returns204_AndHardDeletes()
    {
        var (_, _, trId) = await SeedTransferAsync();

        var response = await _client.DeleteAsync($"/api/transfers/{trId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Transfers.FindAsync(trId)).Should().BeNull();
        _seededTransferIds.Remove(trId);
    }
}
```

- [ ] **Step 2: Run — expect 5 FAILs**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransfersCrudApiTests"
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Api/TransfersCrudApiTests.cs
git commit -m "test(api): failing tests for Transfers GET/PUT/DELETE :id"
```

---

## Task 9: Implement Transfers `GET /:id`, `PUT /:id`, `DELETE /:id`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/TransfersApiController.cs`

- [ ] **Step 1: Add the three actions**

Append inside the controller:

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<TransferEditDto>> Get(Guid id)
{
    var transfer = await transferService.GetByIdAsync(id);
    if (transfer is null) return NotFound();

    using var scope = HttpContext.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var attachments = await db.TransferAttachments
        .Where(a => a.TransferId == id)
        .Select(a => new AttachmentDto(a.Id, a.FileName, a.SizeBytes, a.ContentType, a.UploadedAt))
        .ToListAsync();

    return new TransferEditDto(
        Id:              transfer.Id,
        Date:            transfer.Date,
        Amount:          transfer.Amount,
        SourceAccountId: transfer.SourceAccountId,
        DestAccountId:   transfer.DestAccountId,
        Description:     transfer.Description,
        IsCleared:       transfer.IsCleared,
        Attachments:     attachments);
}

[HttpPut("{id:guid}")]
public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTransferRequest request)
{
    var existing = await transferService.GetByIdAsync(id);
    if (existing is null) return NotFound();

    if (request.SourceAccountId == request.DestAccountId)
    {
        return UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = "One or more fields are invalid.",
                details = new[]
                {
                    new { field = nameof(request.DestAccountId), message = "Source and destination accounts must be different." }
                }
            }
        });
    }

    var vm = new TransferEditViewModel
    {
        Id              = id,
        Date            = request.Date,
        Amount          = request.Amount,
        SourceAccountId = request.SourceAccountId,
        DestAccountId   = request.DestAccountId,
        Description     = request.Description,
        IsCleared       = request.IsCleared
    };

    try
    {
        await transferService.UpdateAsync(vm);
        return NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                details = Array.Empty<object>()
            }
        });
    }
}

[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id)
{
    var existing = await transferService.GetByIdAsync(id);
    if (existing is null) return NotFound();
    await transferService.DeleteAsync(id);
    return NoContent();
}
```

Add usings:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
```

- [ ] **Step 2: Run — expect all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransfersCrudApiTests"
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransfersApiController.cs
git commit -m "feat(api): Transfers GET/PUT/DELETE :id with cross-field validation"
```

---

## Task 10: Failing tests for LiabilityPayments `GET /:id`, `PUT /:id`, `DELETE /:id`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Api/LiabilityPaymentsCrudApiTests.cs`

- [ ] **Step 1: Create the test class**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class LiabilityPaymentsCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededPaymentIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public LiabilityPaymentsCrudApiTests(TestWebApplicationFactory factory)
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
            await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid AssetId, Guid LiabilityId, Guid PaymentId)> SeedPaymentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = new Account { Id = Guid.NewGuid(), Name = $"Lp-Asset-{Guid.NewGuid():N}",     AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var liab  = new Account { Id = Guid.NewGuid(), Name = $"Lp-Liab-{Guid.NewGuid():N}",      AccountTypeId = 2, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(asset, liab);
        _seededAccountIds.Add(asset.Id);
        _seededAccountIds.Add(liab.Id);

        var lp = new LiabilityPayment
        {
            Id                  = Guid.NewGuid(),
            Date                = new DateOnly(2026, 4, 12),
            Amount              = 150m,
            AssetAccountId      = asset.Id,
            LiabilityAccountId  = liab.Id,
            Description         = "Initial payment",
            IsCleared           = false
        };
        db.LiabilityPayments.Add(lp);
        _seededPaymentIds.Add(lp.Id);

        await db.SaveChangesAsync();
        return (asset.Id, liab.Id, lp.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (assetId, liabId, paymentId) = await SeedPaymentAsync();

        var response = await _client.GetAsync($"/api/liability-payments/{paymentId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(paymentId);
        body.GetProperty("assetAccountId").GetGuid().Should().Be(assetId);
        body.GetProperty("liabilityAccountId").GetGuid().Should().Be(liabId);
        body.GetProperty("amount").GetDecimal().Should().Be(150m);
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/liability-payments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Returns204_AndUpdates()
    {
        var (assetId, liabId, paymentId) = await SeedPaymentAsync();

        var response = await _client.PutAsJsonAsync($"/api/liability-payments/{paymentId}", new
        {
            date                = "2026-04-18",
            amount              = 222m,
            assetAccountId      = assetId,
            liabilityAccountId  = liabId,
            description         = "Updated",
            isCleared           = true
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.LiabilityPayments.FindAsync(paymentId);
        saved!.Amount.Should().Be(222m);
        saved.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Returns204_AndHardDeletes()
    {
        var (_, _, paymentId) = await SeedPaymentAsync();

        var response = await _client.DeleteAsync($"/api/liability-payments/{paymentId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.LiabilityPayments.FindAsync(paymentId)).Should().BeNull();
        _seededPaymentIds.Remove(paymentId);
    }
}
```

- [ ] **Step 2: Run — expect all FAIL**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~LiabilityPaymentsCrudApiTests"
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/Api/LiabilityPaymentsCrudApiTests.cs
git commit -m "test(api): failing tests for LiabilityPayments GET/PUT/DELETE :id"
```

---

## Task 11: Implement LiabilityPayments `GET /:id`, `PUT /:id`, `DELETE /:id`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs`

- [ ] **Step 1: Append the three actions**

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<LiabilityPaymentEditDto>> Get(Guid id)
{
    var payment = await liabilityPaymentService.GetByIdAsync(id);
    if (payment is null) return NotFound();

    return new LiabilityPaymentEditDto(
        Id:                 payment.Id,
        Date:               payment.Date,
        Amount:             payment.Amount,
        AssetAccountId:     payment.AssetAccountId,
        LiabilityAccountId: payment.LiabilityAccountId,
        Description:        payment.Description,
        IsCleared:          payment.IsCleared);
}

[HttpPut("{id:guid}")]
public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLiabilityPaymentRequest request)
{
    var existing = await liabilityPaymentService.GetByIdAsync(id);
    if (existing is null) return NotFound();

    var vm = new TransactionEditViewModel
    {
        Id                  = id,
        TransactionType     = "LiabilityPayment",
        Date                = request.Date,
        Amount              = request.Amount,
        AccountId           = request.AssetAccountId,
        LiabilityAccountId  = request.LiabilityAccountId,
        Description         = request.Description,
        IsCleared           = request.IsCleared
    };

    try
    {
        await liabilityPaymentService.UpdateAsync(vm);
        return NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                details = Array.Empty<object>()
            }
        });
    }
}

[HttpDelete("{id:guid}")]
public async Task<IActionResult> Delete(Guid id)
{
    var existing = await liabilityPaymentService.GetByIdAsync(id);
    if (existing is null) return NotFound();
    await liabilityPaymentService.DeleteAsync(id);
    return NoContent();
}
```

- [ ] **Step 2: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~LiabilityPaymentsCrudApiTests"
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Controllers/Api/LiabilityPaymentsApiController.cs
git commit -m "feat(api): LiabilityPayments GET/PUT/DELETE :id"
```

---

## Task 12: Type filter on `GET /api/movements`

**Files:**
- Modify: `ProjectCeres/Services/IMovementService.cs`
- Modify: `ProjectCeres/Services/MovementService.cs`
- Modify: `ProjectCeres/Controllers/Api/MovementsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/MovementsTypeFilterTests.cs`

- [ ] **Step 1: Failing test**

Create `MovementsTypeFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class MovementsTypeFilterTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededPaymentIds = [];

    public MovementsTypeFilterTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)    await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededPaymentIds.Count > 0)     await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task SeedOneOfEachAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a1 = new Account { Id = Guid.NewGuid(), Name = $"Tf-A1-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var a2 = new Account { Id = Guid.NewGuid(), Name = $"Tf-A2-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var liab = new Account { Id = Guid.NewGuid(), Name = $"Tf-L-{Guid.NewGuid():N}", AccountTypeId = 2, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(a1, a2, liab);
        _seededAccountIds.AddRange([a1.Id, a2.Id, liab.Id]);

        var tx = new Transaction { Id = Guid.NewGuid(), Date = date, Amount = 10m, AccountId = a1.Id, CategoryId = HousingCategoryId, Description = "tx-only" };
        var tr = new Transfer    { Id = Guid.NewGuid(), Date = date, Amount = 20m, SourceAccountId = a1.Id, DestAccountId = a2.Id, Description = "tr-only" };
        var lp = new LiabilityPayment { Id = Guid.NewGuid(), Date = date, Amount = 30m, AssetAccountId = a1.Id, LiabilityAccountId = liab.Id, Description = "lp-only" };
        db.Transactions.Add(tx);
        db.Transfers.Add(tr);
        db.LiabilityPayments.Add(lp);
        _seededTransactionIds.Add(tx.Id);
        _seededTransferIds.Add(tr.Id);
        _seededPaymentIds.Add(lp.Id);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMovements_FilterByTransaction_ReturnsOnlyTransactions()
    {
        var date = new DateOnly(2026, 4, 22);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=transaction");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "Transaction");
    }

    [Fact]
    public async Task GetMovements_FilterByTransfer_ReturnsOnlyTransfers()
    {
        var date = new DateOnly(2026, 4, 23);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=transfer");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "Transfer");
    }

    [Fact]
    public async Task GetMovements_FilterByLiabilityPayment_ReturnsOnlyPayments()
    {
        var date = new DateOnly(2026, 4, 24);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=liabilitypayment");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "LiabilityPayment");
    }

    [Fact]
    public async Task GetMovements_InvalidType_Returns400()
    {
        var response = await _client.GetAsync($"/api/movements?type=bogus");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run — expect 4 FAILs**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsTypeFilterTests"
```

- [ ] **Step 3: Extend the service interface and implementation**

`ProjectCeres/Services/IMovementService.cs` — replace contents:

```csharp
using ProjectCeres.Models;
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
        string? q = null,
        MovementType? type = null);

    Task<int> CountAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? q = null,
        MovementType? type = null);

    Task<MovementType?> GetTypeAsync(Guid id);
}
```

`ProjectCeres/Services/MovementService.cs` — replace `GetRecentAsync`, `CountAsync` and add `GetTypeAsync`:

```csharp
public async Task<List<MovementListItemViewModel>> GetRecentAsync(
    Guid? accountId = null,
    DateOnly? from = null,
    DateOnly? to = null,
    int limit = 50,
    int offset = 0,
    string? q = null,
    MovementType? type = null)
{
    var transactions = type is null or MovementType.Transaction
        ? await QueryTransactions(accountId, from, to, q)
        : new List<MovementListItemViewModel>();
    var transfers = type is null or MovementType.Transfer
        ? await QueryTransfers(accountId, from, to, q)
        : new List<MovementListItemViewModel>();
    var payments = type is null or MovementType.LiabilityPayment
        ? await QueryLiabilityPayments(accountId, from, to, q)
        : new List<MovementListItemViewModel>();

    return transactions
        .Concat(transfers)
        .Concat(payments)
        .OrderByDescending(m => m.Date)
        .ThenByDescending(m => m.CreatedAt)
        .Skip(offset)
        .Take(limit)
        .ToList();
}

public async Task<int> CountAsync(
    Guid? accountId = null,
    DateOnly? from = null,
    DateOnly? to = null,
    string? q = null,
    MovementType? type = null)
{
    var t = type is null or MovementType.Transaction
        ? (await QueryTransactions(accountId, from, to, q)).Count : 0;
    var tr = type is null or MovementType.Transfer
        ? (await QueryTransfers(accountId, from, to, q)).Count : 0;
    var lp = type is null or MovementType.LiabilityPayment
        ? (await QueryLiabilityPayments(accountId, from, to, q)).Count : 0;
    return t + tr + lp;
}

public async Task<MovementType?> GetTypeAsync(Guid id)
{
    if (await _db.Transactions.AnyAsync(t => t.Id == id))     return MovementType.Transaction;
    if (await _db.Transfers.AnyAsync(t => t.Id == id))        return MovementType.Transfer;
    if (await _db.LiabilityPayments.AnyAsync(p => p.Id == id)) return MovementType.LiabilityPayment;
    return null;
}
```

- [ ] **Step 4: Wire the controller to accept `type` and parse it**

In `ProjectCeres/Controllers/Api/MovementsApiController.cs`, replace the `GetMovements` action body:

```csharp
[HttpGet]
public async Task<ActionResult<MovementsPageDto>> GetMovements(
    [FromQuery] string? q = null,
    [FromQuery] Guid? accountId = null,
    [FromQuery] DateOnly? from = null,
    [FromQuery] DateOnly? to = null,
    [FromQuery] string? type = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50)
{
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 50;
    if (pageSize > 200) pageSize = 200;

    MovementType? typedFilter = null;
    if (!string.IsNullOrWhiteSpace(type))
    {
        typedFilter = type.ToLowerInvariant() switch
        {
            "transaction"      => MovementType.Transaction,
            "transfer"         => MovementType.Transfer,
            "liabilitypayment" => MovementType.LiabilityPayment,
            _ => null
        };
        if (typedFilter is null)
            return BadRequest(new { error = new { code = "INVALID_TYPE", message = "type must be 'transaction', 'transfer', or 'liabilitypayment'." } });
    }

    var offset = (page - 1) * pageSize;
    var items = await movementService.GetRecentAsync(accountId, from, to, pageSize, offset, q, typedFilter);
    var total = await movementService.CountAsync(accountId, from, to, q, typedFilter);

    var dtoItems = items.Select(MapToDto).ToList();
    return new MovementsPageDto(dtoItems, total, page, pageSize);
}
```

- [ ] **Step 5: Run — all 4 PASS, plus existing MovementsApiTests still PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsApiTests|FullyQualifiedName~MovementsTypeFilterTests"
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/IMovementService.cs ProjectCeres/Services/MovementService.cs ProjectCeres/Controllers/Api/MovementsApiController.cs ProjectCeres.Tests/Integration/Api/MovementsTypeFilterTests.cs
git commit -m "feat(api): type filter on GET /api/movements (transaction|transfer|liabilitypayment)"
```

---

## Task 13: `GET /api/movements/{id}` discriminator endpoint

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MovementsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/MovementsDiscriminatorTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class MovementsDiscriminatorTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];

    public MovementsDiscriminatorTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)    await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_ReturnsTransaction_WhenIdIsTransaction()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"D-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 25), Amount = 5m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.Add(tx); _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/movements/{tx.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("movementType").GetString().Should().Be("Transaction");
        body.GetProperty("id").GetGuid().Should().Be(tx.Id);
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/movements/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run — expect FAILs**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsDiscriminatorTests"
```

- [ ] **Step 3: Add the action**

In `MovementsApiController.cs`, append:

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<MovementTypeDto>> GetType(Guid id)
{
    var type = await movementService.GetTypeAsync(id);
    if (type is null) return NotFound();
    return new MovementTypeDto(id, type.ToString()!);
}
```

- [ ] **Step 4: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsDiscriminatorTests"
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/MovementsApiController.cs ProjectCeres.Tests/Integration/Api/MovementsDiscriminatorTests.cs
git commit -m "feat(api): GET /api/movements/:id discriminator returns {id, movementType}"
```

---

## Task 14: Align all three services on `Task<int> BulkMarkClearedAsync(...)`

**Files:**
- Modify (likely): `ProjectCeres/Services/ITransactionService.cs` and `TransactionService.cs`
- Modify (likely): `ProjectCeres/Services/ITransferService.cs` and `TransferService.cs`
- Modify (likely): `ProjectCeres/Services/ILiabilityPaymentService.cs` and `LiabilityPaymentService.cs`

This task is mandatory because Task 15 calls `BulkMarkClearedAsync` on all three services and sums the returned counts. By the end of this task, each interface MUST expose:

```csharp
Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null);
```

- [ ] **Step 1: Audit existing surfaces**

```bash
grep -n "BulkMarkCleared" ProjectCeres/Services/I*.cs ProjectCeres/Services/*.cs
```

Note for each service whether the method exists and its current return type (`Task` vs `Task<int>`).

- [ ] **Step 2: For each service that does NOT yet expose `Task<int> BulkMarkClearedAsync(...)`, add it**

For Transfers and Liability Payments (if missing), add the interface method and implementation. For an interface that already has the method but returns `Task` (void), change the return type to `Task<int>` and update the implementation to return the affected row count from `ExecuteUpdateAsync`.

Add to each interface that needs it:

```csharp
Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null);
```

And implement in `TransferService.cs`:

```csharp
public async Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null)
{
    var query = _db.Transfers.Where(t => !t.IsCleared && t.Date >= from && t.Date <= to);
    if (accountId.HasValue)
        query = query.Where(t => t.SourceAccountId == accountId.Value || t.DestAccountId == accountId.Value);
    return await query.ExecuteUpdateAsync(s => s.SetProperty(t => t.IsCleared, true));
}
```

And in `LiabilityPaymentService.cs`:

```csharp
public async Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null)
{
    var query = _db.LiabilityPayments.Where(p => !p.IsCleared && p.Date >= from && p.Date <= to);
    if (accountId.HasValue)
        query = query.Where(p => p.AssetAccountId == accountId.Value || p.LiabilityAccountId == accountId.Value);
    return await query.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCleared, true));
}
```

If `ITransactionService.BulkMarkClearedAsync` returns `Task` (not `Task<int>`), update its signature and implementation to return `Task<int>` for consistency with the bulk-cleared endpoint contract — this is a pure return-value change, no callers will break (the existing Razor controller can ignore the int).

- [ ] **Step 3: Build**

```bash
dotnet build ProjectCeres
```

Expected: PASS.

- [ ] **Step 4: Commit (only if changes were made)**

```bash
git add -A ProjectCeres/Services/
git commit -m "feat(services): BulkMarkClearedAsync on Transfer and LiabilityPayment services"
```

If no changes were needed, skip this step.

---

## Task 15: `POST /api/movements/bulk-cleared`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MovementsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/MovementsBulkClearedTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class MovementsBulkClearedTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public MovementsBulkClearedTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Post_MarksMatchingTransactionsCleared()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"BC-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx1 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 5), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        var tx2 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 8), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.AddRange(tx1, tx2);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.AddRange([tx1.Id, tx2.Id]);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            accountId = account.Id,
            type = "transaction"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cleared").GetInt32().Should().Be(2);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Transactions.FindAsync(tx1.Id))!.IsCleared.Should().BeTrue();
        (await verifyDb.Transactions.FindAsync(tx2.Id))!.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Post_Returns400_WhenInvalidType()
    {
        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            type = "bogus"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run — expect FAILs**

- [ ] **Step 3: Implement the controller action**

In `MovementsApiController.cs`, append:

```csharp
[HttpPost("bulk-cleared")]
public async Task<ActionResult<object>> BulkCleared([FromBody] BulkClearedRequest request)
{
    MovementType? typedFilter = null;
    if (!string.IsNullOrWhiteSpace(request.Type))
    {
        typedFilter = request.Type.ToLowerInvariant() switch
        {
            "transaction"      => MovementType.Transaction,
            "transfer"         => MovementType.Transfer,
            "liabilitypayment" => MovementType.LiabilityPayment,
            _ => null
        };
        if (typedFilter is null)
            return BadRequest(new { error = new { code = "INVALID_TYPE", message = "type must be 'transaction', 'transfer', or 'liabilitypayment'." } });
    }

    var total = 0;
    if (typedFilter is null or MovementType.Transaction)
        total += await transactionService.BulkMarkClearedAsync(request.From, request.To, request.AccountId);
    if (typedFilter is null or MovementType.Transfer)
        total += await transferService.BulkMarkClearedAsync(request.From, request.To, request.AccountId);
    if (typedFilter is null or MovementType.LiabilityPayment)
        total += await liabilityPaymentService.BulkMarkClearedAsync(request.From, request.To, request.AccountId);

    return Ok(new { cleared = total });
}
```

Task 14 must have aligned all three services on `Task<int>` — if not, go back and complete Task 14 before continuing.

- [ ] **Step 4: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsBulkClearedTests"
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/MovementsApiController.cs ProjectCeres.Tests/Integration/Api/MovementsBulkClearedTests.cs
git commit -m "feat(api): POST /api/movements/bulk-cleared"
```

---

## Task 16: CSV export service

**Files:**
- Create: `ProjectCeres/Services/IMovementExportService.cs`
- Create: `ProjectCeres/Services/MovementExportService.cs`
- Modify: `ProjectCeres/Program.cs` (DI registration)

- [ ] **Step 1: Service interface**

```csharp
// ProjectCeres/Services/IMovementExportService.cs
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IMovementExportService
{
    Task<string> BuildCsvAsync(
        Guid? accountId,
        DateOnly? from,
        DateOnly? to,
        string? q,
        MovementType? type);
}
```

- [ ] **Step 2: Implementation**

```csharp
// ProjectCeres/Services/MovementExportService.cs
using System.Globalization;
using System.Text;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class MovementExportService(IMovementService movementService) : IMovementExportService
{
    public async Task<string> BuildCsvAsync(
        Guid? accountId,
        DateOnly? from,
        DateOnly? to,
        string? q,
        MovementType? type)
    {
        // Pull the entire matching set; no pagination.
        var rows = await movementService.GetRecentAsync(accountId, from, to, int.MaxValue, 0, q, type);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Type,Amount,Currency,Account,Counterparty,Category,Description,Cleared");

        foreach (var r in rows)
        {
            string typeLabel = r.MovementType switch
            {
                MovementType.Transaction      => "Transaction",
                MovementType.Transfer         => "Transfer",
                MovementType.LiabilityPayment => "Liability payment",
                _ => "?"
            };

            string account = r.MovementType switch
            {
                MovementType.Transaction      => r.AccountName ?? "",
                MovementType.Transfer         => r.SourceAccountName ?? "",
                MovementType.LiabilityPayment => r.AssetAccountName ?? "",
                _ => ""
            };

            string counterparty = r.MovementType switch
            {
                MovementType.Transfer         => r.DestAccountName ?? "",
                MovementType.LiabilityPayment => r.LiabilityAccountName ?? "",
                _ => ""
            };

            string category = r.MovementType == MovementType.Transaction ? r.CategoryName ?? "" : "";

            sb.Append(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(typeLabel)).Append(',');
            sb.Append(r.Amount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Csv(r.CurrencySymbol ?? "")).Append(',');
            sb.Append(Csv(account)).Append(',');
            sb.Append(Csv(counterparty)).Append(',');
            sb.Append(Csv(category)).Append(',');
            sb.Append(Csv(r.Description ?? "")).Append(',');
            sb.AppendLine(r.IsCleared ? "true" : "false");
        }
        return sb.ToString();
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
```

- [ ] **Step 3: Register in DI**

In `ProjectCeres/Program.cs`, find the line that registers `IMovementService` (e.g. `builder.Services.AddScoped<IMovementService, MovementService>();`) and add immediately below:

```csharp
builder.Services.AddScoped<IMovementExportService, MovementExportService>();
```

- [ ] **Step 4: Build**

```bash
dotnet build ProjectCeres
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/IMovementExportService.cs ProjectCeres/Services/MovementExportService.cs ProjectCeres/Program.cs
git commit -m "feat(services): MovementExportService builds CSV body for filtered movements"
```

---

## Task 17: `GET /api/movements/export.csv`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/MovementsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/MovementsExportTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class MovementsExportTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public MovementsExportTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Export_Returns200_WithCsvBody()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Ex-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 11), Amount = 7m, AccountId = account.Id, CategoryId = HousingCategoryId, Description = "row,with,commas" };
        db.Accounts.Add(account); db.Transactions.Add(tx);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/movements/export.csv?from=2026-04-01&to=2026-04-30&type=transaction");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Date,Type,Amount");
        body.Should().Contain("\"row,with,commas\"");
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

- [ ] **Step 3: Inject the export service and add the action**

In `MovementsApiController.cs`, change the constructor signature:

```csharp
public class MovementsApiController(
    IMovementService movementService,
    ITransactionService transactionService,
    ITransferService transferService,
    ILiabilityPaymentService liabilityPaymentService,
    IMovementExportService exportService) : ControllerBase
```

Append the action:

```csharp
[HttpGet("export.csv")]
public async Task<IActionResult> Export(
    [FromQuery] string? q = null,
    [FromQuery] Guid? accountId = null,
    [FromQuery] DateOnly? from = null,
    [FromQuery] DateOnly? to = null,
    [FromQuery] string? type = null)
{
    MovementType? typedFilter = null;
    if (!string.IsNullOrWhiteSpace(type))
    {
        typedFilter = type.ToLowerInvariant() switch
        {
            "transaction"      => MovementType.Transaction,
            "transfer"         => MovementType.Transfer,
            "liabilitypayment" => MovementType.LiabilityPayment,
            _ => null
        };
        if (typedFilter is null)
            return BadRequest(new { error = new { code = "INVALID_TYPE", message = "type must be 'transaction', 'transfer', or 'liabilitypayment'." } });
    }

    var csv = await exportService.BuildCsvAsync(accountId, from, to, q, typedFilter);
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
    return File(bytes, "text/csv", "movements.csv");
}
```

- [ ] **Step 4: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~MovementsExportTests"
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/MovementsApiController.cs ProjectCeres.Tests/Integration/Api/MovementsExportTests.cs
git commit -m "feat(api): GET /api/movements/export.csv"
```

---

## Task 18: Failing tests for `POST /api/transactions/{id}/attachments`

**Files:**
- Create: `ProjectCeres.Tests/Integration/Api/TransactionAttachmentsApiTests.cs`

- [ ] **Step 1: Test class with multipart upload**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransactionAttachmentsApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededAttachmentIds = [];

    public TransactionAttachmentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededAttachmentIds.Count > 0)  await db.TransactionAttachments.Where(a => _seededAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTransactionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Att-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 16), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.Add(tx);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();
        return tx.Id;
    }

    private static MultipartFormDataContent SmallPng()
    {
        // A 1x1 PNG (89 50 4E 47 0D 0A 1A 0A …), enough to satisfy magic-byte validation.
        byte[] png = [
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,
            0x00,0x00,0x00,0x0D,0x49,0x44,0x41,0x54,
            0x78,0x9C,0x62,0x00,0x01,0x00,0x00,0x05,
            0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,
            0x60,0x82
        ];
        var content = new MultipartFormDataContent();
        var bc = new ByteArrayContent(png);
        bc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(bc, "file", "tiny.png");
        return content;
    }

    [Fact]
    public async Task Post_Returns201_AndReturnsAttachmentMetadata()
    {
        var txId = await SeedTransactionAsync();

        var response = await _client.PostAsync($"/api/transactions/{txId}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("tiny.png");
        body.GetProperty("contentType").GetString().Should().Be("image/png");
        var attId = body.GetProperty("id").GetGuid();
        _seededAttachmentIds.Add(attId);
    }

    [Fact]
    public async Task Post_Returns404_WhenTransactionMissing()
    {
        var response = await _client.PostAsync($"/api/transactions/{Guid.NewGuid()}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns204_AndRemovesAttachment()
    {
        var txId = await SeedTransactionAsync();

        var post = await _client.PostAsync($"/api/transactions/{txId}/attachments", SmallPng());
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var attId = posted.GetProperty("id").GetGuid();

        var del = await _client.DeleteAsync($"/api/transactions/attachments/{attId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TransactionAttachments.FindAsync(attId)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run — expect FAILs**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionAttachmentsApiTests"
```

- [ ] **Step 3: Commit failing tests**

```bash
git add ProjectCeres.Tests/Integration/Api/TransactionAttachmentsApiTests.cs
git commit -m "test(api): failing tests for Transaction attachment upload + delete"
```

---

## Task 19: Implement `POST /api/transactions/{id}/attachments` and the DELETE

**Files:**
- Modify: `ProjectCeres/Controllers/Api/TransactionsApiController.cs`

- [ ] **Step 1: Inject the attachment service**

Update the controller's primary constructor:

```csharp
public class TransactionsApiController(
    ITransactionService transactionService,
    IFileAttachmentService attachmentService) : ControllerBase
```

Add `using ProjectCeres.Services;` if missing.

- [ ] **Step 2: Add the two actions**

Append:

```csharp
[HttpPost("{id:guid}/attachments")]
public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
{
    var existing = await transactionService.GetByIdForEditAsync(id);
    if (existing is null) return NotFound();

    try
    {
        await attachmentService.ValidateAsync(file);
        var saved = await attachmentService.UploadAsync(id, file);
        return Created($"/api/transactions/{id}/attachments/{saved.Id}", new
        {
            id          = saved.Id,
            fileName    = saved.FileName,
            sizeBytes   = saved.SizeBytes,
            contentType = saved.ContentType
        });
    }
    catch (InvalidOperationException ex)
    {
        return UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                details = Array.Empty<object>()
            }
        });
    }
}

[HttpDelete("attachments/{attachmentId:guid}")]
public async Task<IActionResult> DeleteAttachment(Guid attachmentId)
{
    try
    {
        await attachmentService.DeleteAsync(attachmentId);
        return NoContent();
    }
    catch (KeyNotFoundException)
    {
        return NotFound();
    }
}
```

If `IFileAttachmentService.DeleteAsync` does not throw `KeyNotFoundException` for missing ids, replace the catch with whatever exception the existing implementation raises, or pre-check existence with a lightweight db query — pick the option that mirrors the existing Razor `AttachmentsController`. Run a quick:

```bash
grep -n "DeleteAsync" ProjectCeres/Services/FileAttachmentService.cs
```

…and align.

- [ ] **Step 3: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransactionAttachmentsApiTests"
```

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Controllers/Api/TransactionsApiController.cs
git commit -m "feat(api): POST /api/transactions/:id/attachments and DELETE /attachments/:id"
```

---

## Task 20: Transfer attachments — failing tests + implementation

**Files:**
- Create: `ProjectCeres.Tests/Integration/Api/TransferAttachmentsApiTests.cs`
- Modify: `ProjectCeres/Controllers/Api/TransfersApiController.cs`

- [ ] **Step 1: Failing tests**

Mirror `TransactionAttachmentsApiTests` for transfers — same shape, paths `/api/transfers/{id}/attachments` and `/api/transfers/attachments/{attachmentId}`. The seeding helper builds two asset accounts and a transfer. Use `SmallPng()` reused/duplicated from Task 18 (duplicate the helper to keep test files independent — DRY at the test class level only).

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class TransferAttachmentsApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAttachmentIds = [];

    public TransferAttachmentsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededAttachmentIds.Count > 0) await db.TransferAttachments.Where(a => _seededAttachmentIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)   await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)    await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTransferAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var src  = new Account { Id = Guid.NewGuid(), Name = $"At-S-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var dest = new Account { Id = Guid.NewGuid(), Name = $"At-D-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(src, dest);
        _seededAccountIds.AddRange([src.Id, dest.Id]);
        var tr = new Transfer { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 17), Amount = 50m, SourceAccountId = src.Id, DestAccountId = dest.Id };
        db.Transfers.Add(tr);
        _seededTransferIds.Add(tr.Id);
        await db.SaveChangesAsync();
        return tr.Id;
    }

    private static MultipartFormDataContent SmallPng()
    {
        byte[] png = [
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
            0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
            0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,
            0x00,0x00,0x00,0x0D,0x49,0x44,0x41,0x54,
            0x78,0x9C,0x62,0x00,0x01,0x00,0x00,0x05,
            0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,
            0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,
            0x60,0x82
        ];
        var content = new MultipartFormDataContent();
        var bc = new ByteArrayContent(png);
        bc.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(bc, "file", "tiny.png");
        return content;
    }

    [Fact]
    public async Task Post_Returns201_AndReturnsAttachmentMetadata()
    {
        var trId = await SeedTransferAsync();
        var response = await _client.PostAsync($"/api/transfers/{trId}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("tiny.png");
        _seededAttachmentIds.Add(body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Post_Returns404_WhenTransferMissing()
    {
        var response = await _client.PostAsync($"/api/transfers/{Guid.NewGuid()}/attachments", SmallPng());
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns204_AndRemovesAttachment()
    {
        var trId = await SeedTransferAsync();
        var post = await _client.PostAsync($"/api/transfers/{trId}/attachments", SmallPng());
        var posted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var attId = posted.GetProperty("id").GetGuid();

        var del = await _client.DeleteAsync($"/api/transfers/attachments/{attId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.TransferAttachments.FindAsync(attId)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run — expect FAILs**

- [ ] **Step 3: Inject the attachment service into Transfers controller**

```csharp
public class TransfersApiController(
    ITransferService transferService,
    IFileAttachmentService attachmentService) : ControllerBase
```

Append the two actions (mirrors the Transactions ones, but uses `UploadForTransferAsync` and `DeleteTransferAttachmentAsync`):

```csharp
[HttpPost("{id:guid}/attachments")]
public async Task<IActionResult> UploadAttachment(Guid id, IFormFile file)
{
    var existing = await transferService.GetByIdAsync(id);
    if (existing is null) return NotFound();

    try
    {
        await attachmentService.ValidateAsync(file);
        var saved = await attachmentService.UploadForTransferAsync(id, file);
        return Created($"/api/transfers/{id}/attachments/{saved.Id}", new
        {
            id          = saved.Id,
            fileName    = saved.FileName,
            sizeBytes   = saved.SizeBytes,
            contentType = saved.ContentType
        });
    }
    catch (InvalidOperationException ex)
    {
        return UnprocessableEntity(new
        {
            error = new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                details = Array.Empty<object>()
            }
        });
    }
}

[HttpDelete("attachments/{attachmentId:guid}")]
public async Task<IActionResult> DeleteAttachment(Guid attachmentId)
{
    try
    {
        await attachmentService.DeleteTransferAttachmentAsync(attachmentId);
        return NoContent();
    }
    catch (KeyNotFoundException)
    {
        return NotFound();
    }
}
```

Same `KeyNotFoundException` caveat as Task 19 — align with whatever the existing service raises.

- [ ] **Step 4: Run — all PASS**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~TransferAttachmentsApiTests"
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/Api/TransferAttachmentsApiTests.cs ProjectCeres/Controllers/Api/TransfersApiController.cs
git commit -m "feat(api): POST /api/transfers/:id/attachments and DELETE /attachments/:id"
```

---

## Task 21: Final regression — run the entire test suite

**Files:** none.

- [ ] **Step 1: Run every test, including the existing ones**

```bash
dotnet test ProjectCeres.Tests
```

Expected: every test passes. If any pre-existing test fails, investigate — the Movement service signature change in Task 12 may have a stale call site somewhere we missed. Likely candidates: `TransactionsController` (Razor), `MovementsController` (Razor), `DashboardApiController`. Search:

```bash
grep -rn "movementService.GetRecentAsync\|movementService.CountAsync\|IMovementService" ProjectCeres/ --include="*.cs"
```

Update any callers to pass `null` for the new `type` parameter (or omit it — it's defaulted).

- [ ] **Step 2: Build the whole solution to confirm no broken refs**

```bash
dotnet build
```

Expected: PASS.

- [ ] **Step 3: If anything was fixed, commit**

```bash
git add -A
git commit -m "fix(callers): pass null type parameter to movement service after signature change"
```

If nothing changed, skip this step.

---

## Self-review checklist (engineer running this plan should tick once)

- [ ] §5.1 of the spec lists 14 new endpoints. Count them in this plan: Tx GET/PUT/DELETE (3) + Tx attachments POST/DELETE (2) + Tr GET/PUT/DELETE (3) + Tr attachments POST/DELETE (2) + Lp GET/PUT/DELETE (3) + Movements bulk-cleared (1) + Movements export (1) = **15**. Discrepancy: the spec table folds the discriminator into a "modified endpoint" implicitly — Task 13 adds it. Plan covers everything. ✅
- [ ] §5.2 modified `GET /api/movements` with `type` — Task 12. ✅
- [ ] §5.3 DTO shapes match Task 2. ✅
- [ ] §5.4 cross-field validation — Task 9 (Transfer source≠dest), Task 11 (LiabilityPayment InvalidOperationException catch). ✅
- [ ] No `BulkMarkClearedAsync` on TransferService/LiabilityPaymentService caught and addressed in Task 14. ✅

---

## What plan 1 ships

After Task 21:

- 15 server endpoints exist and are integration-tested.
- `IMovementService` carries the `MovementType?` filter.
- The SPA still renders only via the existing endpoints — nothing on the client has changed.
- The Razor side still works.

Plan 2 will wire the SPA against this contract.
