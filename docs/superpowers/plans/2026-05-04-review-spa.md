# Review SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder `/app/review` route with a unified Review SPA covering the existing Reconciliations and Transfers queues, retire the two Razor controllers, and clean up the related service interfaces.

**Architecture:** Single `/app/review` route hosting a shadcn `Tabs` shell (`Reconciliations` · `Transfers`) wired to `?tab=` query state and an A3 default-tab rule. A new `ReviewCountProvider` (sibling of `ReminderCountProvider`) feeds the Sidebar combined badge and the per-tab badges from the two existing `pending/count` endpoints. Reconciliations: inline `Confirm match` button + `Dispute` row menu (AlertDialog) + top `Confirm all` (AlertDialog). Transfers: three buttons per card (`Link to existing`, `Create transfer`, `Dismiss`); Link/Create open a shared `TransferActionDialog` with an account picker pre-filtered to same-currency, active, non-self accounts; Dismiss fires immediately. Server: drop throwing CRUD variants from both review services, add `TryConfirmAllAsync`, enrich both staged DTOs with `AccountCurrencyCode` + `AccountCurrencySymbol`, slim both Razor controllers to redirects.

**Tech Stack:** React 19 + TypeScript + Vite + shadcn/ui (Tabs, AlertDialog, DropdownMenu, Popover+Command picker, Skeleton, Sonner), Vitest + React Testing Library, ASP.NET Core (.NET 10), EF Core (Npgsql), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-05-04-review-spa-design.md`.

---

## Sequencing rationale

Server first, then client. Server tasks (1–6) get the API into its final shape so client tasks build against the real wire format (notably `AccountCurrencyCode`/`AccountCurrencySymbol`, which the Transfers picker depends on). Each commit is green; tests are written first per task. After all tasks the integration commit (Task 22) wires everything into `App.tsx`, runs the full verification gate, and lands the changes as one final commit per the spec.

**Commit cadence:** Tasks 1-21 commit independently as small green steps. Task 22 is the final atomic commit for the cutover (Razor controllers → redirects, view deletions, sidebar Razor-badge removal, doc sync). This gives small reviewable history while still landing the user-visible cutover atomically per the spec's "Single commit" intent for the SPA migration.

---

## File structure

### Create — server

| Path | Responsibility |
|---|---|
| (none — only modifications) | |

### Modify — server

| Path | Responsibility |
|---|---|
| `ProjectCeres/ViewModels/ReconciliationReviewApiDtos.cs` | Add `AccountCurrencyCode` + `AccountCurrencySymbol` to `StagedTransactionDto` |
| `ProjectCeres/ViewModels/TransferReviewApiDtos.cs` | Add `AccountCurrencyCode` + `AccountCurrencySymbol` to `StagedTransferDto` |
| `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs` | Project new currency fields; migrate `ConfirmAll` to `TryConfirmAllAsync` |
| `ProjectCeres/Controllers/Api/TransferReviewApiController.cs` | Project new currency fields |
| `ProjectCeres/Services/ITransferReviewService.cs` | Drop throwing CRUD methods |
| `ProjectCeres/Services/TransferReviewService.cs` | Drop throwing method bodies; verify `Account.Currency` eager-load |
| `ProjectCeres/Services/IImportStagedTransactionService.cs` | Drop throwing CRUD methods; add `Task<Result> TryConfirmAllAsync()` |
| `ProjectCeres/Services/ImportStagedTransactionService.cs` | Drop throwing method bodies; implement `TryConfirmAllAsync`; verify eager-load |
| `ProjectCeres/Controllers/TransferReviewController.cs` | Slim to 4 redirects (Task 22) |
| `ProjectCeres/Controllers/ReconciliationReviewController.cs` | Slim to 4 redirects (Task 22) |
| `ProjectCeres/Views/Shared/_Layout.cshtml` | Drop the two pending-queue count badges (Task 22) |

### Delete — server (Task 22)

| Path | Reason |
|---|---|
| `ProjectCeres/Views/TransferReview/Index.cshtml` | Razor view replaced by SPA |
| `ProjectCeres/Views/ReconciliationReview/Index.cshtml` | Razor view replaced by SPA |
| `ProjectCeres/ViewModels/StagedTransferViewModel.cs` | Razor-only |
| `ProjectCeres/ViewModels/StagedTransactionViewModel.cs` | Razor-only |

### Create — client

All under `ProjectCeres.Client/src/app/`.

| Path | Responsibility |
|---|---|
| `features/review/review-api.ts` | URL builders + DTO types (logic-free) |
| `features/review/ReviewCountProvider.tsx` | Context: `{ reconciliationCount, transferCount, total, loading, refresh }` |
| `features/review/ReviewCountProvider.test.tsx` | Provider behavior |
| `features/review/ReviewLayout.tsx` | h1 + description + Tabs shell + A3 default + `?tab=` sync |
| `features/review/ReviewLayout.test.tsx` | Layout behavior + tab routing + first-paint flicker tests |
| `features/review/ReconciliationList.tsx` | Cards + Confirm-all button + empty/loading/error |
| `features/review/ReconciliationList.test.tsx` | List behavior |
| `features/review/ReconciliationCard.tsx` | Single card + inline Confirm + ⋯ menu |
| `features/review/ReconciliationCard.test.tsx` | Card behavior |
| `features/review/ReconciliationDisputeDialog.tsx` | AlertDialog body for Dispute |
| `features/review/ReconciliationDisputeDialog.test.tsx` | Dialog behavior |
| `features/review/ReconciliationConfirmAllDialog.tsx` | AlertDialog body for Confirm-all |
| `features/review/ReconciliationConfirmAllDialog.test.tsx` | Dialog behavior |
| `features/review/TransferList.tsx` | Cards + empty/loading/error |
| `features/review/TransferList.test.tsx` | List behavior |
| `features/review/TransferCard.tsx` | Single card + three buttons |
| `features/review/TransferCard.test.tsx` | Card behavior + Dismiss flight + double-click prevention |
| `features/review/TransferActionDialog.tsx` | Shared Link/Create dialog with picker |
| `features/review/TransferActionDialog.test.tsx` | Dialog behavior + currency-mismatch toast |

### Modify — client

| Path | Responsibility |
|---|---|
| `pages/Review.tsx` | Replace placeholder with one-line re-export of `ReviewLayout` |
| `App.tsx` | Wrap shell in `<ReviewCountProvider>` (sibling of `<ReminderCountProvider>`) |
| `App.test.tsx` | Update placeholder Review test to expect live page |
| `layout/nav-items.ts` | Add optional `useBadge?: () => number | null` field to `NavItem`; wire on Review |
| `layout/Sidebar.tsx` | `SidebarLink` reads `item.useBadge?.()` and renders a count pill when present |
| `layout/Sidebar.test.tsx` | Assert Review badge renders when total > 0, hidden when 0, aria-label includes count |

---

## Tasks

### Task 1: Add `TryConfirmAllAsync` to the staged-transaction service interface

**Files:**
- Modify: `ProjectCeres/Services/IImportStagedTransactionService.cs`
- Test: `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs`

- [ ] **Step 1: Write the failing test for the happy path**

Add to `ImportStagedTransactionServiceTests.cs`:

```csharp
[Fact]
public async Task TryConfirmAllAsync_confirms_all_pending_rows()
{
    using var fx = await TestFixture.CreateAsync();
    var account = await fx.SeedAccountAsync("Checking");
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);

    fx.Db.ImportStagedTransactions.AddRange(
        NewPending(account.Id, matched.Id, fx.UserId, amount: 50m),
        NewPending(account.Id, matched.Id, fx.UserId, amount: 51m),
        NewPending(account.Id, matched.Id, fx.UserId, amount: 52m));
    await fx.Db.SaveChangesAsync();

    var sut = fx.Resolve<IImportStagedTransactionService>();
    var result = await sut.TryConfirmAllAsync();

    result.IsSuccess.Should().BeTrue();
    var rows = await fx.Db.ImportStagedTransactions.ToListAsync();
    rows.Should().AllSatisfy(r =>
    {
        r.Status.Should().Be(StagedTransactionStatus.Confirmed);
        r.ResolvedAt.Should().NotBeNull();
    });
}

private static ImportStagedTransaction NewPending(Guid accountId, Guid matchedId, Guid userId, decimal amount) => new()
{
    Id = Guid.NewGuid(),
    ImportedAt = DateTime.UtcNow,
    AccountId = accountId,
    UserId = userId,
    RawDate = new DateOnly(2026, 5, 4),
    RawAmount = amount,
    RawDescription = "x",
    MatchedTransactionId = matchedId,
    Status = StagedTransactionStatus.Pending,
};
```

- [ ] **Step 2: Run the test to confirm it fails**

Run: `dotnet test --filter ImportStagedTransactionServiceTests.TryConfirmAllAsync_confirms_all_pending_rows`
Expected: FAIL with `IImportStagedTransactionService` does not contain a definition for `TryConfirmAllAsync`.

- [ ] **Step 3: Add the interface method**

Edit `ProjectCeres/Services/IImportStagedTransactionService.cs`:

```csharp
public interface IImportStagedTransactionService
{
    // Razor-era methods (throwing).
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task ConfirmAsync(Guid id);
    Task ConfirmAllAsync();
    Task DisputeAsync(Guid id);

    // API surface (Result-returning).
    Task<Result> TryConfirmAsync(Guid id);
    Task<Result> TryConfirmAllAsync();           // ← NEW
    Task<Result> TryDisputeAsync(Guid id);
}
```

- [ ] **Step 4: Implement `TryConfirmAllAsync` in the service**

Edit `ProjectCeres/Services/ImportStagedTransactionService.cs` — add the method (paste below the existing `TryDisputeAsync`):

```csharp
public async Task<Result> TryConfirmAllAsync()
{
    var userId = currentUserAccessor.Get().UserId;
    var pending = await db.ImportStagedTransactions
        .Where(t => t.Status == StagedTransactionStatus.Pending && t.UserId == userId)
        .ToListAsync();

    var now = DateTime.UtcNow;
    foreach (var row in pending)
    {
        row.Status = StagedTransactionStatus.Confirmed;
        row.ResolvedAt = now;
    }
    await db.SaveChangesAsync();
    return Result.Ok();
}
```

If `currentUserAccessor` is named differently in this service (verify by reading the existing `TryConfirmAsync` body — it almost certainly resolves the user the same way), use the existing pattern.

- [ ] **Step 5: Run the test to confirm it passes**

Run: `dotnet test --filter ImportStagedTransactionServiceTests.TryConfirmAllAsync_confirms_all_pending_rows`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/IImportStagedTransactionService.cs \
        ProjectCeres/Services/ImportStagedTransactionService.cs \
        ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs
git commit -m "feat(staging): add TryConfirmAllAsync to staged transaction service"
```

---

### Task 2: Add idempotency + multi-tenancy + ResolvedAt tests for `TryConfirmAllAsync`

**Files:**
- Test: `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs`

- [ ] **Step 1: Write the four additional tests**

```csharp
[Fact]
public async Task TryConfirmAllAsync_is_idempotent_on_empty_queue()
{
    using var fx = await TestFixture.CreateAsync();
    var sut = fx.Resolve<IImportStagedTransactionService>();
    var result = await sut.TryConfirmAllAsync();
    result.IsSuccess.Should().BeTrue();
}

[Fact]
public async Task TryConfirmAllAsync_sets_ResolvedAt_on_every_confirmed_row()
{
    using var fx = await TestFixture.CreateAsync();
    var account = await fx.SeedAccountAsync("Checking");
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);
    fx.Db.ImportStagedTransactions.AddRange(
        NewPending(account.Id, matched.Id, fx.UserId, amount: 50m),
        NewPending(account.Id, matched.Id, fx.UserId, amount: 51m));
    await fx.Db.SaveChangesAsync();

    var before = DateTime.UtcNow;
    await fx.Resolve<IImportStagedTransactionService>().TryConfirmAllAsync();
    var after = DateTime.UtcNow;

    var rows = await fx.Db.ImportStagedTransactions.ToListAsync();
    rows.Should().AllSatisfy(r =>
    {
        r.ResolvedAt.Should().NotBeNull();
        r.ResolvedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    });
}

[Fact]
public async Task TryConfirmAllAsync_does_not_touch_already_resolved_rows()
{
    using var fx = await TestFixture.CreateAsync();
    var account = await fx.SeedAccountAsync("Checking");
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);

    var alreadyConfirmed = NewPending(account.Id, matched.Id, fx.UserId, amount: 50m);
    alreadyConfirmed.Status = StagedTransactionStatus.Confirmed;
    var fixedResolvedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    alreadyConfirmed.ResolvedAt = fixedResolvedAt;

    var alreadyDisputed = NewPending(account.Id, matched.Id, fx.UserId, amount: 51m);
    alreadyDisputed.Status = StagedTransactionStatus.Disputed;
    alreadyDisputed.ResolvedAt = fixedResolvedAt;

    fx.Db.ImportStagedTransactions.AddRange(
        alreadyConfirmed,
        alreadyDisputed,
        NewPending(account.Id, matched.Id, fx.UserId, amount: 52m));
    await fx.Db.SaveChangesAsync();

    await fx.Resolve<IImportStagedTransactionService>().TryConfirmAllAsync();

    var reloadedConfirmed = await fx.Db.ImportStagedTransactions.FindAsync(alreadyConfirmed.Id);
    var reloadedDisputed  = await fx.Db.ImportStagedTransactions.FindAsync(alreadyDisputed.Id);
    reloadedConfirmed!.ResolvedAt.Should().Be(fixedResolvedAt); // untouched
    reloadedDisputed!.ResolvedAt.Should().Be(fixedResolvedAt);  // untouched
    reloadedDisputed.Status.Should().Be(StagedTransactionStatus.Disputed); // not flipped
}

[Fact]
public async Task TryConfirmAllAsync_does_not_confirm_other_users_pending_rows()
{
    using var fx = await TestFixture.CreateAsync();
    var account = await fx.SeedAccountAsync("Checking");
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);

    var ownPending      = NewPending(account.Id, matched.Id, fx.UserId, amount: 50m);
    var intruderUserId  = Guid.NewGuid();
    var intruderPending = NewPending(account.Id, matched.Id, intruderUserId, amount: 51m);
    fx.Db.ImportStagedTransactions.AddRange(ownPending, intruderPending);
    await fx.Db.SaveChangesAsync();

    await fx.Resolve<IImportStagedTransactionService>().TryConfirmAllAsync();

    (await fx.Db.ImportStagedTransactions.FindAsync(ownPending.Id))!.Status
        .Should().Be(StagedTransactionStatus.Confirmed);
    (await fx.Db.ImportStagedTransactions.FindAsync(intruderPending.Id))!.Status
        .Should().Be(StagedTransactionStatus.Pending);
}
```

- [ ] **Step 2: Run the four new tests**

Run: `dotnet test --filter "FullyQualifiedName~ImportStagedTransactionServiceTests.TryConfirmAllAsync"`
Expected: all four PASS (the implementation in Task 1 already filters by `Status == Pending` and `UserId == userId`).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs
git commit -m "test(staging): TryConfirmAllAsync respects status, user scope, ResolvedAt"
```

---

### Task 3: Migrate `ConfirmAll` API endpoint to `TryConfirmAllAsync`

**Files:**
- Modify: `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs`
- Test: `ProjectCeres.Tests/Integration/Api/ReconciliationReviewApiTests.cs`

- [ ] **Step 1: Write a failing test asserting `confirm-all` still returns 204 after the migration**

Add (or augment, if it already exists) in `ReconciliationReviewApiTests.cs`:

```csharp
[Fact]
public async Task ConfirmAll_returns_204_with_pending_rows_via_TryConfirmAllAsync()
{
    var fx = await ApiFixture.CreateAsync();
    var account = await fx.SeedAccountAsync("Checking");
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);
    await fx.SeedStagedTransactionAsync(account.Id, matched.Id, status: StagedTransactionStatus.Pending);
    await fx.SeedStagedTransactionAsync(account.Id, matched.Id, status: StagedTransactionStatus.Pending);

    var response = await fx.Client.PostAsync("/api/reconciliation-review/confirm-all", null);

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var scope = fx.App.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var rows = await db.ImportStagedTransactions.ToListAsync();
    rows.Should().AllSatisfy(r => r.Status.Should().Be(StagedTransactionStatus.Confirmed));
}
```

If `ApiFixture` does not already expose helpers like `SeedStagedTransactionAsync`, add them by mirroring the existing `SeedTransactionAsync` pattern.

- [ ] **Step 2: Run it; confirm it passes against the *current* throwing `ConfirmAllAsync`**

Run: `dotnet test --filter ConfirmAll_returns_204_with_pending_rows_via_TryConfirmAllAsync`
Expected: PASS (existing endpoint already returns 204; the test is forward-compatible with the migration).

- [ ] **Step 3: Migrate the endpoint to call `TryConfirmAllAsync`**

Edit `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs` — replace the body of `ConfirmAll`:

```csharp
[HttpPost("confirm-all")]
public async Task<IActionResult> ConfirmAll()
{
    var result = await stagedService.TryConfirmAllAsync();
    return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
}
```

- [ ] **Step 4: Run the test again to confirm the migration is still green**

Run: `dotnet test --filter ConfirmAll_returns_204_with_pending_rows_via_TryConfirmAllAsync`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs \
        ProjectCeres.Tests/Integration/Api/ReconciliationReviewApiTests.cs
git commit -m "refactor(api): migrate /reconciliation-review/confirm-all to TryConfirmAllAsync"
```

---

### Task 4: Drop throwing variants from `ITransferReviewService`

**Files:**
- Modify: `ProjectCeres/Services/ITransferReviewService.cs`
- Modify: `ProjectCeres/Services/TransferReviewService.cs`
- Modify: `ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs`

- [ ] **Step 1: Sanity-check that no Razor controller still calls these**

Run:
```bash
grep -rn "TransferReviewService" ProjectCeres ProjectCeres.Tests --include='*.cs' | \
  grep -E "LinkToExistingAsync|CreateAsTransferAsync|DismissAsTransactionAsync" | \
  grep -v "Try"
```
Expected: only matches inside `Controllers/TransferReviewController.cs` and `Services/TransferReviewService.cs` itself (the deleted method bodies). If any other file calls them, stop and inspect — Task 22 may need to address it.

- [ ] **Step 2: Drop the throwing methods from the interface**

Edit `ProjectCeres/Services/ITransferReviewService.cs`:

```csharp
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface ITransferReviewService
{
    Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryLinkToExistingAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryCreateAsTransferAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryDismissAsTransactionAsync(Guid stagedId);
}
```

- [ ] **Step 3: Drop the throwing method bodies from the implementation**

Edit `ProjectCeres/Services/TransferReviewService.cs` — delete `LinkToExistingAsync`, `CreateAsTransferAsync`, `DismissAsTransactionAsync` method bodies. Compilation will break here only if the Razor controller still calls them; verify Task 22 hasn't been pre-applied (it shouldn't have been). If the build breaks because `TransferReviewController` still calls these, **temporarily** stub them in the controller with `=> throw new NotImplementedException()` so the build is green for this commit; the controller is fully replaced in Task 22.

A cleaner alternative: pre-apply the controller redirect change here for just `TransferReviewController` (then the controller bodies don't reference the deleted methods at all). If you prefer that, do Task 22's `TransferReviewController` redirect as part of this commit and note the partial cutover in the commit message. Either approach is acceptable; pick one and be consistent.

- [ ] **Step 4: Drop tests against the deleted methods**

Edit `ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs` — delete tests whose names target `LinkToExistingAsync` (without `Try`), `CreateAsTransferAsync` (without `Try`), or `DismissAsTransactionAsync` (without `Try`). The `Try*` variant tests stay.

- [ ] **Step 5: Run server tests**

Run: `dotnet test`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/ITransferReviewService.cs \
        ProjectCeres/Services/TransferReviewService.cs \
        ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs
# (and ProjectCeres/Controllers/TransferReviewController.cs if you took the pre-apply path)
git commit -m "refactor(transfer-review): drop throwing CRUD variants — Try* now sole API"
```

---

### Task 5: Drop throwing variants from `IImportStagedTransactionService`

**Files:**
- Modify: `ProjectCeres/Services/IImportStagedTransactionService.cs`
- Modify: `ProjectCeres/Services/ImportStagedTransactionService.cs`
- Modify: `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs`

Mirror of Task 4 for the staged-transaction service.

- [ ] **Step 1: Sanity-check call sites**

```bash
grep -rn "IImportStagedTransactionService\|ImportStagedTransactionService" ProjectCeres ProjectCeres.Tests --include='*.cs' | \
  grep -E "\.ConfirmAsync|\.ConfirmAllAsync|\.DisputeAsync" | \
  grep -v "Try"
```
Expected: only `Controllers/ReconciliationReviewController.cs` (the Razor controller) and the implementation file itself.

- [ ] **Step 2: Drop the throwing methods from the interface**

Edit `ProjectCeres/Services/IImportStagedTransactionService.cs`:

```csharp
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryConfirmAsync(Guid id);
    Task<Result> TryConfirmAllAsync();
    Task<Result> TryDisputeAsync(Guid id);
}
```

- [ ] **Step 3: Drop the throwing method bodies from the implementation**

Edit `ProjectCeres/Services/ImportStagedTransactionService.cs` — delete `ConfirmAsync(Guid id)`, `ConfirmAllAsync()`, `DisputeAsync(Guid id)` method bodies. Same Razor-controller caveat as Task 4: either pre-apply the redirect or stub with `NotImplementedException`.

- [ ] **Step 4: Drop tests against the deleted methods**

Same approach as Task 4 step 4.

- [ ] **Step 5: Run server tests**

Run: `dotnet test`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/IImportStagedTransactionService.cs \
        ProjectCeres/Services/ImportStagedTransactionService.cs \
        ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs
git commit -m "refactor(staging): drop throwing CRUD variants — Try* now sole API"
```

---

### Task 6: Enrich both staged DTOs with account currency

**Files:**
- Modify: `ProjectCeres/ViewModels/ReconciliationReviewApiDtos.cs`
- Modify: `ProjectCeres/ViewModels/TransferReviewApiDtos.cs`
- Modify: `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs`
- Modify: `ProjectCeres/Controllers/Api/TransferReviewApiController.cs`
- Modify: `ProjectCeres/Services/TransferReviewService.cs` (verify eager-load only)
- Modify: `ProjectCeres/Services/ImportStagedTransactionService.cs` (verify eager-load only)
- Test: `ProjectCeres.Tests/Integration/Api/ReconciliationReviewApiTests.cs`
- Test: `ProjectCeres.Tests/Integration/Api/TransferReviewApiTests.cs`

- [ ] **Step 1: Write failing tests in both API test files**

In `ReconciliationReviewApiTests.cs`:

```csharp
[Fact]
public async Task Pending_includes_account_currency_code_and_symbol()
{
    var fx = await ApiFixture.CreateAsync();
    var eur = await fx.GetCurrencyAsync("EUR");
    var account = await fx.SeedAccountAsync("Checking", currencyId: eur.Id);
    var matched = await fx.SeedTransactionAsync(account.Id, amount: 50m, isCleared: true);
    await fx.SeedStagedTransactionAsync(account.Id, matched.Id, status: StagedTransactionStatus.Pending);

    var response = await fx.Client.GetAsync("/api/reconciliation-review/pending");
    response.EnsureSuccessStatusCode();

    var dtos = await response.Content.ReadFromJsonAsync<List<StagedTransactionDto>>();
    dtos.Should().HaveCount(1);
    dtos![0].AccountCurrencyCode.Should().Be("EUR");
    dtos![0].AccountCurrencySymbol.Should().NotBeNullOrEmpty();
}
```

In `TransferReviewApiTests.cs`:

```csharp
[Fact]
public async Task Pending_includes_account_currency_code_and_symbol()
{
    var fx = await ApiFixture.CreateAsync();
    var eur = await fx.GetCurrencyAsync("EUR");
    var account = await fx.SeedAccountAsync("Checking", currencyId: eur.Id);
    await fx.SeedStagedTransferAsync(account.Id, status: StagedTransferStatus.Pending);

    var response = await fx.Client.GetAsync("/api/transfer-review/pending");
    response.EnsureSuccessStatusCode();

    var dtos = await response.Content.ReadFromJsonAsync<List<StagedTransferDto>>();
    dtos.Should().HaveCount(1);
    dtos![0].AccountCurrencyCode.Should().Be("EUR");
    dtos![0].AccountCurrencySymbol.Should().NotBeNullOrEmpty();
}
```

If `ApiFixture` lacks `GetCurrencyAsync` / `SeedStagedTransferAsync` / a `currencyId` override on `SeedAccountAsync`, add them by mirroring existing helpers.

- [ ] **Step 2: Run the new tests; confirm they fail**

Run: `dotnet test --filter Pending_includes_account_currency_code_and_symbol`
Expected: FAIL — `StagedTransactionDto` / `StagedTransferDto` does not contain `AccountCurrencyCode`.

- [ ] **Step 3: Add the two new fields to `StagedTransactionDto`**

Edit `ProjectCeres/ViewModels/ReconciliationReviewApiDtos.cs`:

```csharp
namespace ProjectCeres.ViewModels;

public record StagedTransactionDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    string   AccountCurrencyCode,
    string   AccountCurrencySymbol,
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    MatchedTransactionId,
    string?  MatchedTransactionDescription,
    DateOnly MatchedTransactionDate,
    decimal  MatchedTransactionAmount);
```

- [ ] **Step 4: Add the two new fields to `StagedTransferDto`**

Edit `ProjectCeres/ViewModels/TransferReviewApiDtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record StagedTransferDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    string   AccountCurrencyCode,
    string   AccountCurrencySymbol,
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    CandidateTransactionId,
    string?  CandidateTransactionDescription,
    DateOnly? CandidateTransactionDate,
    decimal? CandidateTransactionAmount);

public record TransferReviewActionRequest(
    [Required] Guid? OtherAccountId);
```

- [ ] **Step 5: Verify the service-side eager-load and update the API projections**

Check `TransferReviewService.GetPendingAsync` — if it does `Include(s => s.Account)` only, add `.ThenInclude(a => a.Currency)`. Same for `ImportStagedTransactionService.GetPendingAsync`. Run a quick grep:

```bash
grep -n "GetPendingAsync\|Include\|ThenInclude" ProjectCeres/Services/TransferReviewService.cs ProjectCeres/Services/ImportStagedTransactionService.cs
```

Then update the controller projections:

`ReconciliationReviewApiController.cs`:

```csharp
[HttpGet("pending")]
public async Task<ActionResult<IEnumerable<StagedTransactionDto>>> GetPending()
{
    var pending = await stagedService.GetPendingAsync();
    return Ok(pending.Select(s => new StagedTransactionDto(
        s.Id, s.ImportedAt,
        s.AccountId, s.Account?.Name ?? s.AccountId.ToString(),
        s.Account!.Currency!.Code,
        s.Account!.Currency!.Symbol,
        s.RawDate, s.RawAmount, s.RawDescription,
        s.MatchedTransactionId,
        s.MatchedTransaction?.Description,
        s.MatchedTransaction?.Date ?? s.RawDate,
        s.MatchedTransaction?.Amount ?? s.RawAmount)));
}
```

`TransferReviewApiController.cs`:

```csharp
[HttpGet("pending")]
public async Task<ActionResult<IEnumerable<StagedTransferDto>>> GetPending()
{
    var pending = await reviewService.GetPendingAsync();
    return Ok(pending.Select(s => new StagedTransferDto(
        s.Id, s.ImportedAt,
        s.AccountId, s.Account?.Name ?? s.AccountId.ToString(),
        s.Account!.Currency!.Code,
        s.Account!.Currency!.Symbol,
        s.RawDate, s.RawAmount, s.RawDescription,
        s.CandidateTransactionId,
        s.CandidateTransaction?.Description,
        s.CandidateTransaction?.Date,
        s.CandidateTransaction?.Amount)));
}
```

- [ ] **Step 6: Run all server tests**

Run: `dotnet test`
Expected: green (the two new tests now PASS; nothing else regresses).

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/ViewModels/ReconciliationReviewApiDtos.cs \
        ProjectCeres/ViewModels/TransferReviewApiDtos.cs \
        ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs \
        ProjectCeres/Controllers/Api/TransferReviewApiController.cs \
        ProjectCeres/Services/TransferReviewService.cs \
        ProjectCeres/Services/ImportStagedTransactionService.cs \
        ProjectCeres.Tests/Integration/Api/ReconciliationReviewApiTests.cs \
        ProjectCeres.Tests/Integration/Api/TransferReviewApiTests.cs
git commit -m "feat(api): include AccountCurrencyCode + Symbol on staged DTOs"
```

---

### Task 7: Create `review-api.ts` (URL builders + DTO types)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/review-api.ts`

This is logic-free; no test file needed for pure constants.

- [ ] **Step 1: Create the file**

```typescript
// URL builders
export const RECONCILIATION_REVIEW_PENDING_URL       = '/api/reconciliation-review/pending';
export const RECONCILIATION_REVIEW_PENDING_COUNT_URL = '/api/reconciliation-review/pending/count';
export const RECONCILIATION_REVIEW_CONFIRM_URL       = (id: string) => `/api/reconciliation-review/${id}/confirm`;
export const RECONCILIATION_REVIEW_CONFIRM_ALL_URL   = '/api/reconciliation-review/confirm-all';
export const RECONCILIATION_REVIEW_DISPUTE_URL       = (id: string) => `/api/reconciliation-review/${id}/dispute`;

export const TRANSFER_REVIEW_PENDING_URL       = '/api/transfer-review/pending';
export const TRANSFER_REVIEW_PENDING_COUNT_URL = '/api/transfer-review/pending/count';
export const TRANSFER_REVIEW_LINK_URL          = (id: string) => `/api/transfer-review/${id}/link-to-existing`;
export const TRANSFER_REVIEW_CREATE_URL        = (id: string) => `/api/transfer-review/${id}/create-as-transfer`;
export const TRANSFER_REVIEW_DISMISS_URL       = (id: string) => `/api/transfer-review/${id}/dismiss-as-transaction`;

// DTO types — mirrors of the C# records in ReconciliationReviewApiDtos.cs and TransferReviewApiDtos.cs
export type StagedTransactionDto = {
  id: string;
  importedAt: string;          // ISO 8601 from server
  accountId: string;
  accountName: string;
  accountCurrencyCode: string;
  accountCurrencySymbol: string;
  rawDate: string;             // ISO date (YYYY-MM-DD) from server DateOnly
  rawAmount: number;
  rawDescription: string | null;
  matchedTransactionId: string | null;
  matchedTransactionDescription: string | null;
  matchedTransactionDate: string;
  matchedTransactionAmount: number;
};

export type StagedTransferDto = {
  id: string;
  importedAt: string;
  accountId: string;
  accountName: string;
  accountCurrencyCode: string;
  accountCurrencySymbol: string;
  rawDate: string;
  rawAmount: number;
  rawDescription: string | null;
  candidateTransactionId: string | null;
  candidateTransactionDescription: string | null;
  candidateTransactionDate: string | null;
  candidateTransactionAmount: number | null;
};

// Wire shape for Link / Create dialog submit
export type TransferReviewActionRequest = {
  otherAccountId: string;
};
```

- [ ] **Step 2: Confirm TS compiles**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/review-api.ts
git commit -m "feat(review): add review-api URL builders and DTO types"
```

---

### Task 8: Create `ReviewCountProvider` with happy-path test

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReviewCountProvider.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReviewCountProvider.test.tsx`

- [ ] **Step 1: Write the test for the happy path**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReviewCountProvider, useReviewCount } from './ReviewCountProvider';

function CountDisplay() {
  const { reconciliationCount, transferCount, total, loading } = useReviewCount();
  return (
    <div>
      <span data-testid="r">{reconciliationCount}</span>
      <span data-testid="t">{transferCount}</span>
      <span data-testid="total">{total}</span>
      <span data-testid="loading">{loading ? '1' : '0'}</span>
    </div>
  );
}

function mockCounts(reconciliation: number, transfer: number) {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => reconciliation } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => transfer } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;
}

describe('ReviewCountProvider', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('provides counts from both endpoints and a derived total', async () => {
    mockCounts(2, 1);
    render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
    await waitFor(() => expect(screen.getByTestId('r').textContent).toBe('2'));
    expect(screen.getByTestId('t').textContent).toBe('1');
    expect(screen.getByTestId('total').textContent).toBe('3');
    expect(screen.getByTestId('loading').textContent).toBe('0');
  });
});
```

- [ ] **Step 2: Run; confirm fail**

Run: `cd ProjectCeres.Client && pnpm test ReviewCountProvider`
Expected: FAIL — module does not exist.

- [ ] **Step 3: Create `ReviewCountProvider.tsx`**

```tsx
import { createContext, useContext, useMemo } from 'react';
import { useApi } from '../../lib/use-api';
import {
  RECONCILIATION_REVIEW_PENDING_COUNT_URL,
  TRANSFER_REVIEW_PENDING_COUNT_URL,
} from './review-api';

type Ctx = {
  reconciliationCount: number;
  transferCount: number;
  total: number;
  loading: boolean;
  refresh: () => void;
};

const ReviewCountContext = createContext<Ctx>({
  reconciliationCount: 0,
  transferCount: 0,
  total: 0,
  loading: false,
  refresh: () => {},
});

export function ReviewCountProvider({ children }: { children: React.ReactNode }) {
  const recon = useApi<number>(RECONCILIATION_REVIEW_PENDING_COUNT_URL);
  const xfer  = useApi<number>(TRANSFER_REVIEW_PENDING_COUNT_URL);

  const value = useMemo<Ctx>(() => {
    const reconciliationCount = recon.error ? 0 : recon.data ?? 0;
    const transferCount       = xfer.error  ? 0 : xfer.data  ?? 0;
    return {
      reconciliationCount,
      transferCount,
      total: reconciliationCount + transferCount,
      loading: recon.loading || xfer.loading,
      refresh: () => { recon.refetch(); xfer.refetch(); },
    };
  }, [recon.data, recon.error, recon.loading, xfer.data, xfer.error, xfer.loading]);

  return <ReviewCountContext.Provider value={value}>{children}</ReviewCountContext.Provider>;
}

export function useReviewCount(): Ctx {
  return useContext(ReviewCountContext);
}
```

- [ ] **Step 4: Run; confirm pass**

Run: `cd ProjectCeres.Client && pnpm test ReviewCountProvider`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReviewCountProvider.tsx \
        ProjectCeres.Client/src/app/features/review/ReviewCountProvider.test.tsx
git commit -m "feat(review): add ReviewCountProvider with happy-path test"
```

---

### Task 9: Add ReviewCountProvider edge-case tests

**Files:**
- Test: `ProjectCeres.Client/src/app/features/review/ReviewCountProvider.test.tsx`

- [ ] **Step 1: Append the four edge-case tests**

```tsx
it('refresh refetches both endpoints', async () => {
  const fetchMock = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => 1 } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => 0 } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  });
  global.fetch = fetchMock as typeof fetch;

  function Trigger() {
    const { refresh } = useReviewCount();
    return <button onClick={refresh}>refresh</button>;
  }

  render(
    <ReviewCountProvider>
      <CountDisplay />
      <Trigger />
    </ReviewCountProvider>,
  );
  await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('1'));
  const before = fetchMock.mock.calls.length;

  screen.getByText('refresh').click();
  await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(before + 1));
});

it('treats an errored single endpoint as count=0; other still works', async () => {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: false, status: 500 } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => 4 } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;

  render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
  await waitFor(() => expect(screen.getByTestId('loading').textContent).toBe('0'));
  expect(screen.getByTestId('r').textContent).toBe('0');
  expect(screen.getByTestId('t').textContent).toBe('4');
  expect(screen.getByTestId('total').textContent).toBe('4');
});

it('loading is true while either fetch is in flight', async () => {
  let resolveRecon: (v: Response) => void;
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return new Promise<Response>((r) => { resolveRecon = r; });
    }
    return Promise.resolve({ ok: true, json: async () => 0 } as Response);
  }) as typeof fetch;

  render(<ReviewCountProvider><CountDisplay /></ReviewCountProvider>);
  // Transfer endpoint settles fast; reconciliation is held open → loading stays true
  await waitFor(() => expect(screen.getByTestId('t').textContent).toBe('0'));
  expect(screen.getByTestId('loading').textContent).toBe('1');

  resolveRecon!({ ok: true, json: async () => 0 } as Response);
  await waitFor(() => expect(screen.getByTestId('loading').textContent).toBe('0'));
});

it('refresh propagates: a downstream consumer reads the new value after refresh', async () => {
  let recon = 1;
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => recon } as Response);
    }
    return Promise.resolve({ ok: true, json: async () => 0 } as Response);
  }) as typeof fetch;

  function Trigger() {
    const { refresh } = useReviewCount();
    return <button onClick={refresh}>refresh</button>;
  }

  render(
    <ReviewCountProvider>
      <CountDisplay />
      <Trigger />
    </ReviewCountProvider>,
  );
  await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('1'));
  recon = 0;                         // simulate the row being mutated server-side
  screen.getByText('refresh').click();
  await waitFor(() => expect(screen.getByTestId('total').textContent).toBe('0'));
});
```

- [ ] **Step 2: Run all `ReviewCountProvider` tests**

Run: `cd ProjectCeres.Client && pnpm test ReviewCountProvider`
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReviewCountProvider.test.tsx
git commit -m "test(review): edge cases for ReviewCountProvider (error, loading, refresh propagation)"
```

---

### Task 10: Reconciliation list — `ReconciliationCard` (display + inline Confirm)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReconciliationCard.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReconciliationCard.test.tsx`

This task covers display + inline `Confirm match`. The `⋯` menu wires to Dispute in Task 12.

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationCard } from './ReconciliationCard';
import type { StagedTransactionDto } from './review-api';

function dto(overrides: Partial<StagedTransactionDto> = {}): StagedTransactionDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    importedAt: '2026-05-04T09:14:00Z',
    accountId: 'aaaa',
    accountName: 'Checking',
    accountCurrencyCode: 'EUR',
    accountCurrencySymbol: '€',
    rawDate: '2026-05-02',
    rawAmount: -500.00,
    rawDescription: 'Transfer to savings',
    matchedTransactionId: 'mmmm',
    matchedTransactionDescription: 'Wire to savings account',
    matchedTransactionDate: '2026-05-02',
    matchedTransactionAmount: -500.00,
    ...overrides,
  };
}

describe('ReconciliationCard', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('renders meta, raw row, description, and matched pill', () => {
    render(<ReconciliationCard staged={dto()} onChanged={() => {}} />);
    expect(screen.getByText(/Checking — imported/)).toBeInTheDocument();
    expect(screen.getByText('Transfer to savings')).toBeInTheDocument();
    expect(screen.getByText(/Matched to:/)).toBeInTheDocument();
  });

  it('Confirm match POSTs to the confirm endpoint and fires success toast on 204', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;
    const onChanged = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/reconciliation-review/11111111-1111-1111-1111-111111111111/confirm',
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText('Confirmed.')).toBeInTheDocument());
  });

  it('404 fires "no longer exists" toast and still calls onChanged so list refetches', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch;
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onChanged).toHaveBeenCalled();
  });

  it('other errors fire generic toast and do NOT call onChanged', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch;
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationCard staged={dto()} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm match/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't confirm/i)).toBeInTheDocument());
    expect(onChanged).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run; confirm fail**

Run: `cd ProjectCeres.Client && pnpm test ReconciliationCard`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `ReconciliationCard.tsx`**

```tsx
import { useState } from 'react';
import { Check, MoreHorizontal } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { RECONCILIATION_REVIEW_CONFIRM_URL, type StagedTransactionDto } from './review-api';

type Props = {
  staged: StagedTransactionDto;
  onChanged: () => void;
};

export function ReconciliationCard({ staged, onChanged }: Props) {
  const [confirming, setConfirming] = useState(false);

  async function handleConfirm() {
    if (confirming) return;
    setConfirming(true);
    try {
      const res = await fetch(RECONCILIATION_REVIEW_CONFIRM_URL(staged.id), { method: 'POST' });
      if (res.ok) {
        toast.success('Confirmed.');
        onChanged();
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onChanged();
      } else {
        toast.error("Couldn't confirm. Try again.");
      }
    } catch {
      toast.error("Couldn't confirm. Try again.");
    } finally {
      setConfirming(false);
    }
  }

  const importedAt = new Date(staged.importedAt);
  const importedAtLabel = importedAt.toLocaleString();      // simple display; user format applied elsewhere
  const amountClass = staged.rawAmount < 0 ? 'text-rose-600' : 'text-emerald-700';
  const matchedAmountClass = staged.matchedTransactionAmount < 0 ? 'text-rose-600' : 'text-emerald-700';

  return (
    <div className="rounded-md border border-border bg-card p-4 shadow-sm">
      <div className="text-xs text-muted-foreground">{staged.accountName} — imported {importedAtLabel}</div>
      <div className="mt-1 flex items-baseline gap-2 text-base font-semibold">
        <span>{staged.rawDate}</span>
        <span className={`tabular-nums ${amountClass}`}>
          {staged.accountCurrencySymbol}{staged.rawAmount.toFixed(2)}
        </span>
      </div>
      {staged.rawDescription && (
        <div className="mt-0.5 text-sm text-muted-foreground">{staged.rawDescription}</div>
      )}
      <div className="mt-2 inline-block rounded bg-sky-50 px-2 py-1 text-sm text-sky-800">
        Matched to: {staged.matchedTransactionDate}{' '}
        <span className={`tabular-nums ${matchedAmountClass}`}>
          {staged.accountCurrencySymbol}{staged.matchedTransactionAmount.toFixed(2)}
        </span>{' '}
        {staged.matchedTransactionDescription ?? ''}
      </div>
      <div className="mt-3 flex items-center justify-end gap-2">
        <Button onClick={handleConfirm} disabled={confirming}>
          <Check className="h-4 w-4" aria-hidden="true" />
          {confirming ? 'Confirming…' : 'Confirm match'}
        </Button>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" aria-label="More actions">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem disabled>Dispute (wired in Task 12)</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  );
}
```

The placeholder `Dropdown` item gets replaced in Task 12. The disabled placeholder lets us ship a green commit now without dead state in the menu.

- [ ] **Step 4: Run; confirm pass**

Run: `cd ProjectCeres.Client && pnpm test ReconciliationCard`
Expected: all four PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReconciliationCard.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationCard.test.tsx
git commit -m "feat(review): ReconciliationCard with inline Confirm match"
```

---

### Task 11: Reconciliation list — `ReconciliationConfirmAllDialog`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReconciliationConfirmAllDialog.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReconciliationConfirmAllDialog.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationConfirmAllDialog } from './ReconciliationConfirmAllDialog';

describe('ReconciliationConfirmAllDialog', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('title shows the captured count from props', () => {
    render(
      <ReconciliationConfirmAllDialog open count={5} onOpenChange={() => {}} onConfirmed={() => {}} />,
    );
    expect(screen.getByText(/Confirm all 5 matches/)).toBeInTheDocument();
  });

  it('submit posts to /confirm-all and on 204 toasts with the count', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;
    const onConfirmed = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationConfirmAllDialog open count={3} onOpenChange={onOpenChange} onConfirmed={onConfirmed} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm all/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      '/api/reconciliation-review/confirm-all',
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText('Confirmed 3 matches.')).toBeInTheDocument());
    expect(onConfirmed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('on error keeps the dialog open and shows generic toast', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch;
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationConfirmAllDialog open count={3} onOpenChange={onOpenChange} onConfirmed={() => {}} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Confirm all/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't confirm all/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
```

- [ ] **Step 2: Implement the dialog**

```tsx
import { useState } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { RECONCILIATION_REVIEW_CONFIRM_ALL_URL } from './review-api';

type Props = {
  open: boolean;
  count: number;                                // captured at dialog-open time
  onOpenChange: (next: boolean) => void;
  onConfirmed: () => void;
};

export function ReconciliationConfirmAllDialog({ open, count, onOpenChange, onConfirmed }: Props) {
  const [submitting, setSubmitting] = useState(false);

  async function handleConfirm() {
    if (submitting) return;
    setSubmitting(true);
    try {
      const res = await fetch(RECONCILIATION_REVIEW_CONFIRM_ALL_URL, { method: 'POST' });
      if (res.ok) {
        toast.success(`Confirmed ${count} matches.`);
        onConfirmed();
        onOpenChange(false);
      } else {
        toast.error("Couldn't confirm all. Try again.");
      }
    } catch {
      toast.error("Couldn't confirm all. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Confirm all {count} matches?</AlertDialogTitle>
          <AlertDialogDescription>
            This accepts every staged match in the list. You can still adjust individual transactions later
            from Movements, but confirming clears them from this queue all at once.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleConfirm} disabled={submitting}>
            {submitting ? 'Confirming…' : 'Confirm all'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 3: Run; confirm pass**

Run: `cd ProjectCeres.Client && pnpm test ReconciliationConfirmAllDialog`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReconciliationConfirmAllDialog.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationConfirmAllDialog.test.tsx
git commit -m "feat(review): ReconciliationConfirmAllDialog"
```

---

### Task 12: Reconciliation `Dispute` flow — dialog + wire into `ReconciliationCard`'s row menu

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReconciliationDisputeDialog.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReconciliationDisputeDialog.test.tsx`
- Modify: `ProjectCeres.Client/src/app/features/review/ReconciliationCard.tsx`
- Modify: `ProjectCeres.Client/src/app/features/review/ReconciliationCard.test.tsx`

- [ ] **Step 1: Write the dialog test**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationDisputeDialog } from './ReconciliationDisputeDialog';

const STAGED_ID = '11111111-1111-1111-1111-111111111111';

describe('ReconciliationDisputeDialog', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('Cancel closes without firing fetch', async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock as typeof fetch;
    const onOpenChange = vi.fn();
    render(
      <ReconciliationDisputeDialog
        open stagedId={STAGED_ID}
        onOpenChange={onOpenChange} onDisputed={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('Submit posts to /dispute and 204 fires success toast + onDisputed + close', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;
    const onDisputed = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog open stagedId={STAGED_ID} onOpenChange={onOpenChange} onDisputed={onDisputed} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/reconciliation-review/${STAGED_ID}/dispute`,
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText(/Disputed\. Original un-cleared and a new transaction added\./)).toBeInTheDocument());
    expect(onDisputed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('404 closes the dialog and still calls onDisputed (so the list refetches)', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch;
    const onDisputed = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog open stagedId={STAGED_ID} onOpenChange={onOpenChange} onDisputed={onDisputed} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onDisputed).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('422 keeps dialog open with generic error toast', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 422 }) as typeof fetch;
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <ReconciliationDisputeDialog open stagedId={STAGED_ID} onOpenChange={onOpenChange} onDisputed={() => {}} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dispute match/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't dispute/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
```

- [ ] **Step 2: Implement `ReconciliationDisputeDialog.tsx`**

```tsx
import { useState } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { RECONCILIATION_REVIEW_DISPUTE_URL } from './review-api';

type Props = {
  open: boolean;
  stagedId: string;
  onOpenChange: (next: boolean) => void;
  onDisputed: () => void;
};

export function ReconciliationDisputeDialog({ open, stagedId, onOpenChange, onDisputed }: Props) {
  const [submitting, setSubmitting] = useState(false);

  async function handleDispute() {
    if (submitting) return;
    setSubmitting(true);
    try {
      const res = await fetch(RECONCILIATION_REVIEW_DISPUTE_URL(stagedId), { method: 'POST' });
      if (res.ok) {
        toast.success('Disputed. Original un-cleared and a new transaction added.');
        onDisputed();
        onOpenChange(false);
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onDisputed();
        onOpenChange(false);
      } else {
        toast.error("Couldn't dispute. Try again.");
      }
    } catch {
      toast.error("Couldn't dispute. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Dispute this match?</AlertDialogTitle>
          <AlertDialogDescription>
            The matched transaction will be marked as not cleared, and this CSV row will be inserted as
            a new transaction marked "Needs review."{' '}
            You can fix mistakes from the Movements page.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleDispute} disabled={submitting}>
            {submitting ? 'Disputing…' : 'Dispute match'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 3: Wire the menu item in `ReconciliationCard`**

Replace the disabled placeholder dropdown item with a real one. Edit `ReconciliationCard.tsx`:

Replace the imports block to add the dialog and `useState` already present:

```tsx
import { ReconciliationDisputeDialog } from './ReconciliationDisputeDialog';
```

Replace the `DropdownMenuContent` block:

```tsx
<DropdownMenuContent align="end">
  <DropdownMenuItem onSelect={(e) => { e.preventDefault(); setDisputeOpen(true); }}>
    Dispute
  </DropdownMenuItem>
</DropdownMenuContent>
```

Add a `disputeOpen` state at the top of the component:

```tsx
const [disputeOpen, setDisputeOpen] = useState(false);
```

And render the dialog at the bottom of the JSX (sibling of the outer div), right before the closing `</>`-or-fragment if needed; if your component returns a single `<div>`, change to a `<>` fragment so you can render the dialog as a sibling.

```tsx
<ReconciliationDisputeDialog
  open={disputeOpen}
  stagedId={staged.id}
  onOpenChange={setDisputeOpen}
  onDisputed={onChanged}
/>
```

- [ ] **Step 4: Add the row-menu test to `ReconciliationCard.test.tsx`**

```tsx
it('row menu opens and shows Dispute item which opens the dialog', async () => {
  render(<ReconciliationCard staged={dto()} onChanged={() => {}} />);
  await userEvent.click(screen.getByRole('button', { name: /More actions/i }));
  await userEvent.click(screen.getByRole('menuitem', { name: /Dispute/i }));
  expect(screen.getByRole('alertdialog', { name: /Dispute this match/i })).toBeInTheDocument();
});
```

- [ ] **Step 5: Run all card + dialog tests**

Run: `cd ProjectCeres.Client && pnpm test ReconciliationCard ReconciliationDisputeDialog`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReconciliationDisputeDialog.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationDisputeDialog.test.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationCard.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationCard.test.tsx
git commit -m "feat(review): Dispute dialog + wire into ReconciliationCard row menu"
```

---

### Task 13: `ReconciliationList` — fetch, render cards, Confirm-all button, empty/loading/error

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReconciliationList.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReconciliationList.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { ReconciliationList } from './ReconciliationList';

function mockGet(rows: unknown[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true, status: 200, json: async () => rows,
  }) as typeof fetch;
}

describe('ReconciliationList', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('shows skeletons while loading', () => {
    global.fetch = vi.fn(() => new Promise(() => {})) as typeof fetch;
    render(<ReconciliationList onChanged={() => {}} />);
    expect(screen.getAllByTestId('reconciliation-skeleton').length).toBeGreaterThan(0);
  });

  it('renders one card per row', async () => {
    mockGet([
      { id: '1', importedAt: '2026-05-04T09:14:00Z', accountId: 'a', accountName: 'Checking',
        accountCurrencyCode: 'EUR', accountCurrencySymbol: '€',
        rawDate: '2026-05-02', rawAmount: -1, rawDescription: 'a',
        matchedTransactionId: 'm1', matchedTransactionDescription: null,
        matchedTransactionDate: '2026-05-02', matchedTransactionAmount: -1 },
      { id: '2', importedAt: '2026-05-04T09:14:00Z', accountId: 'a', accountName: 'Checking',
        accountCurrencyCode: 'EUR', accountCurrencySymbol: '€',
        rawDate: '2026-05-02', rawAmount: -2, rawDescription: 'b',
        matchedTransactionId: 'm2', matchedTransactionDescription: null,
        matchedTransactionDate: '2026-05-02', matchedTransactionAmount: -2 },
    ]);
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getAllByRole('button', { name: /Confirm match/i }).length).toBe(2));
  });

  it('Confirm-all button visible when rows exist; opens dialog on click', async () => {
    mockGet([{
      id: '1', importedAt: '2026-05-04T09:14:00Z', accountId: 'a', accountName: 'Checking',
      accountCurrencyCode: 'EUR', accountCurrencySymbol: '€',
      rawDate: '2026-05-02', rawAmount: -1, rawDescription: null,
      matchedTransactionId: 'm', matchedTransactionDescription: null,
      matchedTransactionDate: '2026-05-02', matchedTransactionAmount: -1,
    }]);
    render(<ReconciliationList onChanged={() => {}} />);
    const button = await screen.findByRole('button', { name: /Confirm all/i });
    await userEvent.click(button);
    expect(screen.getByRole('alertdialog', { name: /Confirm all 1 matches?/ })).toBeInTheDocument();
  });

  it('renders empty state when no rows', async () => {
    mockGet([]);
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/No reconciliations to review/)).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Confirm all/i })).toBeNull();
  });

  it('renders CardError on fetch failure with retry', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch;
    render(<ReconciliationList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/Couldn't load Reconciliations/)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Retry/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Implement `ReconciliationList.tsx`**

```tsx
import { useState } from 'react';
import { Check } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '@/app/components/CardError';
import { useApi } from '../../lib/use-api';
import { RECONCILIATION_REVIEW_PENDING_URL, type StagedTransactionDto } from './review-api';
import { ReconciliationCard } from './ReconciliationCard';
import { ReconciliationConfirmAllDialog } from './ReconciliationConfirmAllDialog';

type Props = {
  /** Called after any successful mutation so the parent count provider can refresh. */
  onChanged: () => void;
};

export function ReconciliationList({ onChanged }: Props) {
  const list = useApi<StagedTransactionDto[]>(RECONCILIATION_REVIEW_PENDING_URL);
  const [confirmAllOpen, setConfirmAllOpen] = useState(false);
  const [capturedCount, setCapturedCount] = useState(0);

  function handleChange() {
    list.refetch();
    onChanged();
  }

  if (list.loading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-20 w-full" data-testid="reconciliation-skeleton" />
        ))}
      </div>
    );
  }

  if (list.error) {
    return <CardError section="Reconciliations" onRetry={list.refetch} />;
  }

  const rows = list.data ?? [];

  if (rows.length === 0) {
    return (
      <div className="py-12 text-center">
        <p className="text-base font-medium">No reconciliations to review.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          The importer hasn't auto-matched any rows that need your confirmation.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="mb-4 flex items-start justify-between gap-4">
        <p className="text-sm text-muted-foreground">
          These rows were automatically matched to existing transactions during import.
          Confirm correct matches or dispute incorrect ones.
        </p>
        <Button
          variant="outline"
          onClick={() => { setCapturedCount(rows.length); setConfirmAllOpen(true); }}
        >
          <Check className="h-4 w-4" aria-hidden="true" />
          Confirm all
        </Button>
      </div>
      <div className="flex flex-col gap-3">
        {rows.map((staged) => (
          <ReconciliationCard key={staged.id} staged={staged} onChanged={handleChange} />
        ))}
      </div>
      <ReconciliationConfirmAllDialog
        open={confirmAllOpen}
        count={capturedCount}
        onOpenChange={setConfirmAllOpen}
        onConfirmed={handleChange}
      />
    </>
  );
}
```

- [ ] **Step 3: Run tests**

Run: `cd ProjectCeres.Client && pnpm test ReconciliationList`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReconciliationList.tsx \
        ProjectCeres.Client/src/app/features/review/ReconciliationList.test.tsx
git commit -m "feat(review): ReconciliationList with cards, Confirm-all, empty/loading/error"
```

---

### Task 14: `TransferCard` — display, conditional Link, immediate Dismiss with flight state and double-click prevention

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/TransferCard.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/TransferCard.test.tsx`

The dialog wires in Task 16. This task ships Dismiss + the static Link/Create buttons (with stub click handlers that the next task replaces).

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { TransferCard } from './TransferCard';
import type { StagedTransferDto } from './review-api';

function dto(overrides: Partial<StagedTransferDto> = {}): StagedTransferDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    importedAt: '2026-05-04T09:14:00Z',
    accountId: 'aaaa',
    accountName: 'Checking',
    accountCurrencyCode: 'EUR',
    accountCurrencySymbol: '€',
    rawDate: '2026-05-02',
    rawAmount: -500.00,
    rawDescription: 'Transfer to savings',
    candidateTransactionId: 'cccc',
    candidateTransactionDescription: 'Incoming transfer',
    candidateTransactionDate: '2026-05-02',
    candidateTransactionAmount: 500.00,
    ...overrides,
  };
}

describe('TransferCard', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('renders meta, raw row, and candidate pill when candidate present', () => {
    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    expect(screen.getByText(/Checking — imported/)).toBeInTheDocument();
    expect(screen.getByText(/Possible match:/)).toBeInTheDocument();
  });

  it('hides Link button and pill when no candidate', () => {
    render(
      <TransferCard
        staged={dto({ candidateTransactionId: null, candidateTransactionDescription: null,
                      candidateTransactionDate: null, candidateTransactionAmount: null })}
        accounts={[]} onChanged={() => {}}
      />,
    );
    expect(screen.queryByText(/Possible match/)).toBeNull();
    expect(screen.queryByRole('button', { name: /Link to existing/i })).toBeNull();
    expect(screen.getByRole('button', { name: /Create transfer/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Dismiss/i })).toBeInTheDocument();
  });

  it('Dismiss POSTs to /dismiss-as-transaction; 204 fires success toast and onChanged', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;
    const onChanged = vi.fn();

    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/transfer-review/${dto().id}/dismiss-as-transaction`,
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByText('Imported as a plain transaction.')).toBeInTheDocument());
  });

  it('Dismiss shows "Dismissing…" and disables button during flight', async () => {
    let resolve!: (v: Response) => void;
    global.fetch = vi.fn(() => new Promise<Response>((r) => { resolve = r; })) as typeof fetch;

    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />
      </>,
    );
    const button = screen.getByRole('button', { name: /Dismiss/i });
    await userEvent.click(button);
    expect(button).toBeDisabled();
    expect(screen.getByText(/Dismissing…/)).toBeInTheDocument();

    resolve({ ok: true, status: 204 } as Response);
    await waitFor(() => expect(button).not.toBeDisabled());
  });

  it('rapid double-click on Dismiss fires only one POST', async () => {
    let resolve!: (v: Response) => void;
    const fetchMock = vi.fn(() => new Promise<Response>((r) => { resolve = r; }));
    global.fetch = fetchMock as typeof fetch;

    render(<TransferCard staged={dto()} accounts={[]} onChanged={() => {}} />);
    const button = screen.getByRole('button', { name: /Dismiss/i });
    await userEvent.click(button);
    await userEvent.click(button);                     // disabled now, should not fire

    expect(fetchMock).toHaveBeenCalledTimes(1);
    resolve({ ok: true, status: 204 } as Response);
  });

  it('Dismiss 404 fires "no longer exists" toast and calls onChanged', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch;
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onChanged).toHaveBeenCalled();
  });

  it('Dismiss other error fires generic toast, does not call onChanged', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch;
    const onChanged = vi.fn();
    render(
      <>
        <Toaster />
        <TransferCard staged={dto()} accounts={[]} onChanged={onChanged} />
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: /Dismiss/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't dismiss/i)).toBeInTheDocument());
    expect(onChanged).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Implement `TransferCard.tsx`**

```tsx
import { useState } from 'react';
import { Link as LinkIcon, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { TRANSFER_REVIEW_DISMISS_URL, type StagedTransferDto } from './review-api';

// Minimal account shape needed by the dialog. Wired up in Task 16.
export type AccountOption = {
  id: string;
  name: string;
  isActive: boolean;
  currencyCode: string;
};

type Props = {
  staged: StagedTransferDto;
  accounts: AccountOption[];          // consumed by the dialog wired in Task 16
  onChanged: () => void;
};

export function TransferCard({ staged, accounts, onChanged }: Props) {
  const [dismissing, setDismissing] = useState(false);

  async function handleDismiss() {
    if (dismissing) return;
    setDismissing(true);
    try {
      const res = await fetch(TRANSFER_REVIEW_DISMISS_URL(staged.id), { method: 'POST' });
      if (res.ok) {
        toast.success('Imported as a plain transaction.');
        onChanged();
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onChanged();
      } else {
        toast.error("Couldn't dismiss. Try again.");
      }
    } catch {
      toast.error("Couldn't dismiss. Try again.");
    } finally {
      setDismissing(false);
    }
  }

  const importedAt = new Date(staged.importedAt).toLocaleString();
  const amountClass = staged.rawAmount < 0 ? 'text-rose-600' : 'text-emerald-700';
  const candidateAmountClass =
    (staged.candidateTransactionAmount ?? 0) < 0 ? 'text-rose-600' : 'text-emerald-700';

  return (
    <div className="rounded-md border border-border bg-card p-4 shadow-sm">
      <div className="text-xs text-muted-foreground">{staged.accountName} — imported {importedAt}</div>
      <div className="mt-1 flex items-baseline gap-2 text-base font-semibold">
        <span>{staged.rawDate}</span>
        <span className={`tabular-nums ${amountClass}`}>
          {staged.accountCurrencySymbol}{staged.rawAmount.toFixed(2)}
        </span>
      </div>
      {staged.rawDescription && (
        <div className="mt-0.5 text-sm text-muted-foreground">{staged.rawDescription}</div>
      )}
      {staged.candidateTransactionId && staged.candidateTransactionAmount !== null && staged.candidateTransactionDate && (
        <div className="mt-2 inline-block rounded bg-sky-50 px-2 py-1 text-sm text-sky-800">
          Possible match: {staged.candidateTransactionDate}{' '}
          <span className={`tabular-nums ${candidateAmountClass}`}>
            {staged.accountCurrencySymbol}{staged.candidateTransactionAmount.toFixed(2)}
          </span>{' '}
          {staged.candidateTransactionDescription ?? ''}
        </div>
      )}
      <div className="mt-3 flex items-center justify-end gap-2">
        {staged.candidateTransactionId && (
          <Button
            // dialog wiring lands in Task 16
            disabled
            aria-label="Link to existing"
          >
            <LinkIcon className="h-4 w-4" aria-hidden="true" />
            Link to existing
          </Button>
        )}
        <Button variant="outline" disabled aria-label="Create transfer">
          <Plus className="h-4 w-4" aria-hidden="true" />
          Create transfer
        </Button>
        <Button
          variant="outline"
          className="text-destructive"
          onClick={handleDismiss}
          disabled={dismissing}
        >
          {dismissing ? 'Dismissing…' : 'Dismiss'}
        </Button>
      </div>
    </div>
  );
}
```

The Link/Create buttons are intentionally disabled here. Task 16 wires the `TransferActionDialog` and removes the `disabled` attributes.

- [ ] **Step 3: Run tests**

Run: `cd ProjectCeres.Client && pnpm test TransferCard`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/TransferCard.tsx \
        ProjectCeres.Client/src/app/features/review/TransferCard.test.tsx
git commit -m "feat(review): TransferCard with display, conditional Link, immediate Dismiss"
```

---

### Task 15: `TransferList` — fetch, render, empty/loading/error

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/TransferList.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/TransferList.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TransferList } from './TransferList';

function mockResponses(rows: unknown[], accounts: unknown[]) {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/api/transfer-review/pending')) {
      return Promise.resolve({ ok: true, json: async () => rows } as Response);
    }
    if (url.endsWith('/api/accounts')) {
      return Promise.resolve({ ok: true, json: async () => accounts } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;
}

describe('TransferList', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('shows skeletons while loading', () => {
    global.fetch = vi.fn(() => new Promise(() => {})) as typeof fetch;
    render(<TransferList onChanged={() => {}} />);
    expect(screen.getAllByTestId('transfer-skeleton').length).toBeGreaterThan(0);
  });

  it('renders one card per row when loaded', async () => {
    mockResponses(
      [
        { id: '1', importedAt: '2026-05-04T09:14:00Z', accountId: 'a', accountName: 'Checking',
          accountCurrencyCode: 'EUR', accountCurrencySymbol: '€',
          rawDate: '2026-05-02', rawAmount: -1, rawDescription: 'a',
          candidateTransactionId: null, candidateTransactionDescription: null,
          candidateTransactionDate: null, candidateTransactionAmount: null },
      ],
      [],
    );
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByRole('button', { name: /Dismiss/i })).toBeInTheDocument());
  });

  it('renders empty state when no rows', async () => {
    mockResponses([], []);
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/No transfers to review/)).toBeInTheDocument());
  });

  it('renders CardError on fetch failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as typeof fetch;
    render(<TransferList onChanged={() => {}} />);
    await waitFor(() => expect(screen.getByText(/Couldn't load Transfers/)).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Implement `TransferList.tsx`**

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '@/app/components/CardError';
import { useApi } from '../../lib/use-api';
import { TRANSFER_REVIEW_PENDING_URL, type StagedTransferDto } from './review-api';
import { TransferCard, type AccountOption } from './TransferCard';

type Props = {
  onChanged: () => void;
};

export function TransferList({ onChanged }: Props) {
  const list     = useApi<StagedTransferDto[]>(TRANSFER_REVIEW_PENDING_URL);
  const accounts = useApi<AccountOption[]>('/api/accounts');

  function handleChange() {
    list.refetch();
    onChanged();
  }

  if (list.loading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-20 w-full" data-testid="transfer-skeleton" />
        ))}
      </div>
    );
  }

  if (list.error) {
    return <CardError section="Transfers" onRetry={list.refetch} />;
  }

  const rows = list.data ?? [];

  if (rows.length === 0) {
    return (
      <div className="py-12 text-center">
        <p className="text-base font-medium">No transfers to review.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          The importer hasn't flagged any rows that look like transfers.
        </p>
      </div>
    );
  }

  return (
    <>
      <p className="mb-4 text-sm text-muted-foreground">
        These rows look like transfers between accounts. Resolve each one before they appear as plain transactions.
      </p>
      <div className="flex flex-col gap-3">
        {rows.map((staged) => (
          <TransferCard
            key={staged.id}
            staged={staged}
            accounts={accounts.data ?? []}
            onChanged={handleChange}
          />
        ))}
      </div>
    </>
  );
}
```

The `AccountOption` shape assumes the existing `/api/accounts` endpoint returns objects with `id`, `name`, `isActive`, and `currencyCode`. **Verify against `AccountListItemDto`** before committing — if the field names differ (e.g. `currency.code` vs `currencyCode`), update the `AccountOption` type and the picker filter to match. The plan assumes the flat-`currencyCode` shape used by the Recurring page.

- [ ] **Step 3: Run tests**

Run: `cd ProjectCeres.Client && pnpm test TransferList`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/TransferList.tsx \
        ProjectCeres.Client/src/app/features/review/TransferList.test.tsx
git commit -m "feat(review): TransferList with cards, empty/loading/error"
```

---

### Task 16: `TransferActionDialog` — shared Link/Create dialog with picker, currency-mismatch toast, and wire into `TransferCard`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/TransferActionDialog.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/TransferActionDialog.test.tsx`
- Modify: `ProjectCeres.Client/src/app/features/review/TransferCard.tsx`

- [ ] **Step 1: Write the failing tests for the dialog**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { Toaster } from 'sonner';
import { TransferActionDialog } from './TransferActionDialog';
import type { AccountOption } from './TransferCard';

const STAGED_ID = '11111111-1111-1111-1111-111111111111';

const accounts: AccountOption[] = [
  { id: 'aa', name: 'Checking',     isActive: true,  currencyCode: 'EUR' },
  { id: 'bb', name: 'Savings',      isActive: true,  currencyCode: 'EUR' },
  { id: 'cc', name: 'USD Account',  isActive: true,  currencyCode: 'USD' },
  { id: 'dd', name: 'Old Closed',   isActive: false, currencyCode: 'EUR' },
];

describe('TransferActionDialog', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('mode="link" renders correct title and button label', () => {
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    expect(screen.getByText(/Link to existing transfer/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Link transfer/i })).toBeInTheDocument();
  });

  it('mode="create" renders correct title and button label', () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    expect(screen.getByText(/^Create transfer$/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Create transfer$/i })).toBeInTheDocument();
  });

  it('picker filters out own account, inactive accounts, and different-currency accounts', async () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Savings')).toBeInTheDocument();           // included
    expect(screen.queryByText('Checking')).toBeNull();                 // own account excluded
    expect(screen.queryByText('USD Account')).toBeNull();              // wrong currency excluded
    expect(screen.queryByText('Old Closed')).toBeNull();               // inactive excluded
  });

  it('shows empty-state in picker when no eligible accounts', async () => {
    render(
      <TransferActionDialog
        open mode="create" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={[
          { id: 'aa', name: 'Checking', isActive: true, currencyCode: 'EUR' },   // own account
          { id: 'cc', name: 'USD',      isActive: true, currencyCode: 'USD' },   // wrong currency
        ]}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText(/No eligible accounts/i)).toBeInTheDocument();
  });

  it('submit button disabled until selection made', () => {
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={() => {}} onActioned={() => {}}
      />,
    );
    expect(screen.getByRole('button', { name: /Link transfer/i })).toBeDisabled();
  });

  it('mode="link" 204 success: posts to /link-to-existing, toast "Linked.", closes', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;
    const onActioned = vi.fn();
    const onOpenChange = vi.fn();

    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={onActioned}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /Link transfer/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/transfer-review/${STAGED_ID}/link-to-existing`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ otherAccountId: 'bb' }),
      }),
    ));
    await waitFor(() => expect(screen.getByText('Linked.')).toBeInTheDocument());
    expect(onActioned).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('mode="create" 204 success: posts to /create-as-transfer, toast "Transfer created.", closes', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204 });
    global.fetch = fetchMock as typeof fetch;

    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="create" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={() => {}} onActioned={() => {}}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /^Create transfer$/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(
      `/api/transfer-review/${STAGED_ID}/create-as-transfer`,
      expect.objectContaining({ method: 'POST' }),
    ));
    await waitFor(() => expect(screen.getByText('Transfer created.')).toBeInTheDocument());
  });

  it('404 closes dialog and calls onActioned (so list refetches)', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 404 }) as typeof fetch;
    const onActioned = vi.fn();
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={onActioned}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /Link transfer/i }));
    await waitFor(() => expect(screen.getByText(/no longer exists/i)).toBeInTheDocument());
    expect(onActioned).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('422 keeps dialog open and shows generic error toast', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false, status: 422,
      json: async () => ({ error: { code: 'GENERIC_FAILURE', message: 'oops', details: [] } }),
    }) as typeof fetch;
    const onOpenChange = vi.fn();
    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={onOpenChange} onActioned={() => {}}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /Link transfer/i }));
    await waitFor(() => expect(screen.getByText(/Couldn't link/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it('422 with currency-mismatch domain code shows currency-specific toast (defensive)', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false, status: 422,
      json: async () => ({ error: { code: 'CURRENCY_MISMATCH', message: 'mismatch', details: [] } }),
    }) as typeof fetch;
    render(
      <>
        <Toaster />
        <TransferActionDialog
          open mode="link" stagedId={STAGED_ID}
          ownAccountId="aa" ownAccountCurrencyCode="EUR"
          accounts={accounts}
          onOpenChange={() => {}} onActioned={() => {}}
        />
      </>,
    );
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByText('Savings'));
    await userEvent.click(screen.getByRole('button', { name: /Link transfer/i }));
    await waitFor(() => expect(screen.getByText(/doesn't share the staged row's currency/i)).toBeInTheDocument());
  });

  it('Cancel closes without firing fetch', async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock as typeof fetch;
    const onOpenChange = vi.fn();
    render(
      <TransferActionDialog
        open mode="link" stagedId={STAGED_ID}
        ownAccountId="aa" ownAccountCurrencyCode="EUR"
        accounts={accounts}
        onOpenChange={onOpenChange} onActioned={() => {}}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /Cancel/i }));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
```

The test assumes the picker is exposed as `role="combobox"` — verify against the locked Popover+Command idiom in the existing Settings or Recurring code. If the trigger uses `role="button"` instead, switch the selector.

**Important on the currency-mismatch domain code:** confirm with `TransferReviewService.TryCreateAsTransferAsync` and `TryLinkToExistingAsync` what `Result.Fail` code they actually return on currency mismatch. The string `CURRENCY_MISMATCH` here is provisional; **read those service methods before writing the test** and use the real code.

- [ ] **Step 2: Implement `TransferActionDialog.tsx`**

```tsx
import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Button } from '@/components/ui/button';
import { ChevronsUpDown } from 'lucide-react';
import {
  TRANSFER_REVIEW_LINK_URL, TRANSFER_REVIEW_CREATE_URL,
} from './review-api';
import type { AccountOption } from './TransferCard';

type Props = {
  open: boolean;
  mode: 'link' | 'create';
  stagedId: string;
  ownAccountId: string;
  ownAccountCurrencyCode: string;
  accounts: AccountOption[];
  onOpenChange: (next: boolean) => void;
  onActioned: () => void;
};

// Replace this with the actual Result.Fail code emitted by TryLinkToExistingAsync
// and TryCreateAsTransferAsync — verified at plan-execution time.
const CURRENCY_MISMATCH_CODE = 'CURRENCY_MISMATCH';

export function TransferActionDialog({
  open, mode, stagedId, ownAccountId, ownAccountCurrencyCode,
  accounts, onOpenChange, onActioned,
}: Props) {
  const [otherAccountId, setOtherAccountId] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const eligible = useMemo(
    () => accounts.filter(
      (a) => a.id !== ownAccountId && a.isActive && a.currencyCode === ownAccountCurrencyCode,
    ),
    [accounts, ownAccountId, ownAccountCurrencyCode],
  );

  const titleText = mode === 'link' ? 'Link to existing transfer' : 'Create transfer';
  const bodyText = mode === 'link'
    ? "Pick the account on the other side of this transfer. We'll link this row to the matching transaction we found there."
    : "Pick the account on the other side of this transfer. We'll create a new transfer record between the two accounts.";
  const submitLabel = mode === 'link' ? 'Link transfer' : 'Create transfer';
  const submitInflightLabel = mode === 'link' ? 'Linking…' : 'Creating…';
  const url = mode === 'link' ? TRANSFER_REVIEW_LINK_URL(stagedId) : TRANSFER_REVIEW_CREATE_URL(stagedId);
  const successToast = mode === 'link' ? 'Linked.' : 'Transfer created.';
  const genericError = mode === 'link' ? "Couldn't link. Try again." : "Couldn't create transfer. Try again.";

  async function handleSubmit() {
    if (!otherAccountId || submitting) return;
    setSubmitting(true);
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ otherAccountId }),
      });
      if (res.ok) {
        toast.success(successToast);
        onActioned();
        onOpenChange(false);
        return;
      }
      if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onActioned();
        onOpenChange(false);
        return;
      }
      if (res.status === 422) {
        const body = await res.json().catch(() => null) as { error?: { code?: string } } | null;
        if (body?.error?.code === CURRENCY_MISMATCH_CODE) {
          toast.error("That account doesn't share the staged row's currency.");
        } else {
          toast.error(genericError);
        }
        return;
      }
      toast.error(genericError);
    } catch {
      toast.error(genericError);
    } finally {
      setSubmitting(false);
    }
  }

  const selectedAccount = eligible.find((a) => a.id === otherAccountId);

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{titleText}</AlertDialogTitle>
          <AlertDialogDescription>{bodyText}</AlertDialogDescription>
        </AlertDialogHeader>

        <div className="my-4 flex flex-col gap-1">
          <label htmlFor="other-account-trigger" className="text-sm font-medium">
            Other account *
          </label>
          <Popover open={pickerOpen} onOpenChange={setPickerOpen}>
            <PopoverTrigger asChild>
              <Button
                id="other-account-trigger"
                role="combobox"
                variant="outline"
                aria-expanded={pickerOpen}
                className="w-full justify-between"
              >
                {selectedAccount?.name ?? '— Select account —'}
                <ChevronsUpDown className="h-4 w-4 opacity-50" />
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-full p-0" align="start">
              <Command>
                <CommandInput placeholder="Search accounts…" />
                <CommandList>
                  <CommandEmpty>
                    No eligible accounts. Transfers must be between accounts of the same currency.
                  </CommandEmpty>
                  <CommandGroup>
                    {eligible.map((a) => (
                      <CommandItem
                        key={a.id}
                        value={a.name}
                        onSelect={() => { setOtherAccountId(a.id); setPickerOpen(false); }}
                      >
                        {a.name}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </CommandList>
              </Command>
            </PopoverContent>
          </Popover>
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleSubmit} disabled={!otherAccountId || submitting}>
            {submitting ? submitInflightLabel : submitLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 3: Wire the dialog into `TransferCard`**

Edit `TransferCard.tsx`:

Add imports:

```tsx
import { TransferActionDialog } from './TransferActionDialog';
```

Add state:

```tsx
const [linkOpen, setLinkOpen]     = useState(false);
const [createOpen, setCreateOpen] = useState(false);
```

Replace the Link button JSX:

```tsx
{staged.candidateTransactionId && (
  <Button onClick={() => setLinkOpen(true)} aria-label="Link to existing">
    <LinkIcon className="h-4 w-4" aria-hidden="true" />
    Link to existing
  </Button>
)}
```

Replace the Create button JSX:

```tsx
<Button variant="outline" onClick={() => setCreateOpen(true)} aria-label="Create transfer">
  <Plus className="h-4 w-4" aria-hidden="true" />
  Create transfer
</Button>
```

Render the two dialogs at the end (sibling of the outer div — switch to a `<>` fragment):

```tsx
<TransferActionDialog
  open={linkOpen} mode="link" stagedId={staged.id}
  ownAccountId={staged.accountId}
  ownAccountCurrencyCode={staged.accountCurrencyCode}
  accounts={accounts}
  onOpenChange={setLinkOpen}
  onActioned={onChanged}
/>
<TransferActionDialog
  open={createOpen} mode="create" stagedId={staged.id}
  ownAccountId={staged.accountId}
  ownAccountCurrencyCode={staged.accountCurrencyCode}
  accounts={accounts}
  onOpenChange={setCreateOpen}
  onActioned={onChanged}
/>
```

- [ ] **Step 4: Run the dialog and card tests**

Run: `cd ProjectCeres.Client && pnpm test TransferActionDialog TransferCard`
Expected: all PASS.

The earlier `TransferCard.test.tsx` tests that asserted `disabled` on Link and Create will now fail. Update those tests so the buttons are no longer expected to be disabled — replace the Step 1 test of Task 14 that asserted `disabled` on Link/Create with assertions that clicking them opens the corresponding dialog. (Or delete the disabled-state assertions from that test if they only existed as scaffolding.)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/TransferActionDialog.tsx \
        ProjectCeres.Client/src/app/features/review/TransferActionDialog.test.tsx \
        ProjectCeres.Client/src/app/features/review/TransferCard.tsx \
        ProjectCeres.Client/src/app/features/review/TransferCard.test.tsx
git commit -m "feat(review): TransferActionDialog with picker, currency-mismatch handling, wired into TransferCard"
```

---

### Task 17: `ReviewLayout` — h1 + description + tabs + ?tab= sync + lazy mount

**Files:**
- Create: `ProjectCeres.Client/src/app/features/review/ReviewLayout.tsx`
- Test: `ProjectCeres.Client/src/app/features/review/ReviewLayout.test.tsx`

This task ships the layout plus the basic tab-routing tests. The first-paint flicker / user-touched tests live in Task 18 since they need a finer-grained test harness.

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ReviewCountProvider } from './ReviewCountProvider';
import { ReviewLayout } from './ReviewLayout';

function renderAt(initialEntry: string, recon: number, xfer: number, transferRows: unknown[] = []) {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => recon } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => xfer } as Response);
    }
    if (url.endsWith('/reconciliation-review/pending')) {
      return Promise.resolve({ ok: true, json: async () => [] } as Response);
    }
    if (url.endsWith('/transfer-review/pending')) {
      return Promise.resolve({ ok: true, json: async () => transferRows } as Response);
    }
    if (url.endsWith('/api/accounts')) {
      return Promise.resolve({ ok: true, json: async () => [] } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;

  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <ReviewCountProvider>
        <Routes>
          <Route path="/review" element={<ReviewLayout />} />
        </Routes>
      </ReviewCountProvider>
    </MemoryRouter>,
  );
}

describe('ReviewLayout', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('renders h1 and description', async () => {
    renderAt('/review', 0, 0);
    expect(screen.getByRole('heading', { name: /^Review$/ })).toBeInTheDocument();
    expect(screen.getByText(/Triage rows the importer staged for you/)).toBeInTheDocument();
  });

  it('both tab triggers always visible; badges hidden when count is 0', async () => {
    renderAt('/review', 0, 0);
    expect(screen.getByRole('tab', { name: /Reconciliations/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Transfers/ })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByTestId('reconciliations-count')).toBeNull();
      expect(screen.queryByTestId('transfers-count')).toBeNull();
    });
  });

  it('badges render when count > 0', async () => {
    renderAt('/review', 2, 1);
    await waitFor(() => expect(screen.getByTestId('reconciliations-count').textContent).toBe('2'));
    expect(screen.getByTestId('transfers-count').textContent).toBe('1');
  });

  it('?tab=transfers selects Transfers tab', async () => {
    renderAt('/review?tab=transfers', 0, 0);
    const transfers = screen.getByRole('tab', { name: /Transfers/ });
    await waitFor(() => expect(transfers).toHaveAttribute('aria-selected', 'true'));
  });

  it('A3: no-param + reconciliations > 0 → Reconciliations active', async () => {
    renderAt('/review', 1, 0);
    const recon = screen.getByRole('tab', { name: /Reconciliations/ });
    await waitFor(() => expect(recon).toHaveAttribute('aria-selected', 'true'));
  });

  it('A3: no-param + reconciliations === 0 + transfers > 0 → Transfers active after counts arrive', async () => {
    renderAt('/review', 0, 1);
    await waitFor(() => expect(screen.getByRole('tab', { name: /Transfers/ })).toHaveAttribute('aria-selected', 'true'));
  });

  it('A3: no-param + both empty → Reconciliations active', async () => {
    renderAt('/review', 0, 0);
    const recon = screen.getByRole('tab', { name: /Reconciliations/ });
    await waitFor(() => expect(recon).toHaveAttribute('aria-selected', 'true'));
  });

  it('clicking Transfers writes ?tab=transfers (replace, not push)', async () => {
    const { container } = renderAt('/review', 1, 0);
    await userEvent.click(screen.getByRole('tab', { name: /Transfers/ }));
    await waitFor(() => expect(window.location.search).toContain('tab=transfers')); // MemoryRouter doesn't sync window; assert via the tab state instead:
    expect(screen.getByRole('tab', { name: /Transfers/ })).toHaveAttribute('aria-selected', 'true');
  });

  it('Transfers fetch only fires after Transfers tab is selected (lazy mount)', async () => {
    const fetchMock = vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) return Promise.resolve({ ok: true, json: async () => 1 } as Response);
      if (url.endsWith('/transfer-review/pending/count'))       return Promise.resolve({ ok: true, json: async () => 0 } as Response);
      if (url.endsWith('/reconciliation-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/transfer-review/pending'))             return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/api/accounts'))                        return Promise.resolve({ ok: true, json: async () => [] } as Response);
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    });
    global.fetch = fetchMock as typeof fetch;

    render(
      <MemoryRouter initialEntries={['/review']}>
        <ReviewCountProvider>
          <Routes>
            <Route path="/review" element={<ReviewLayout />} />
          </Routes>
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByRole('tab', { name: /Reconciliations/ })).toHaveAttribute('aria-selected', 'true'));
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/transfer-review/pending'))).toBe(false);

    await userEvent.click(screen.getByRole('tab', { name: /Transfers/ }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/transfer-review/pending'))).toBe(true));
  });
});
```

- [ ] **Step 2: Implement `ReviewLayout.tsx`**

```tsx
import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useReviewCount } from './ReviewCountProvider';
import { ReconciliationList } from './ReconciliationList';
import { TransferList } from './TransferList';

type TabKey = 'reconciliations' | 'transfers';

function pickDefaultTab(reconciliationCount: number, transferCount: number): TabKey {
  if (reconciliationCount > 0) return 'reconciliations';
  if (transferCount > 0)       return 'transfers';
  return 'reconciliations';
}

function readTabParam(value: string | null): TabKey | null {
  if (value === 'reconciliations' || value === 'transfers') return value;
  return null;
}

export function ReviewLayout() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { reconciliationCount, transferCount, loading, refresh } = useReviewCount();

  const initialFromUrl = readTabParam(searchParams.get('tab'));
  const [activeTab, setActiveTab] = useState<TabKey>(initialFromUrl ?? 'reconciliations');
  const userTouchedTab = useRef(initialFromUrl !== null);

  // A3: post-load adjustment when no explicit ?tab= and the user hasn't clicked.
  useEffect(() => {
    if (initialFromUrl !== null) return;
    if (loading) return;
    if (userTouchedTab.current) return;
    const target = pickDefaultTab(reconciliationCount, transferCount);
    if (target !== activeTab) setActiveTab(target);
  }, [loading, reconciliationCount, transferCount, initialFromUrl, activeTab]);

  function handleTabChange(next: string) {
    const t = (next as TabKey);
    setActiveTab(t);
    userTouchedTab.current = true;
    setSearchParams({ tab: t }, { replace: true });
  }

  return (
    <div className="mx-auto max-w-4xl py-6">
      <h1 className="text-2xl font-semibold" tabIndex={-1}>Review</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Triage rows the importer staged for you. Confirm what's right, dispute what's wrong,
        link transfers to their other side.
      </p>

      <Tabs value={activeTab} onValueChange={handleTabChange} className="mt-6">
        <TabsList>
          <TabsTrigger value="reconciliations">
            Reconciliations
            {reconciliationCount > 0 && (
              <span className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs" data-testid="reconciliations-count">
                {reconciliationCount}
              </span>
            )}
          </TabsTrigger>
          <TabsTrigger value="transfers">
            Transfers
            {transferCount > 0 && (
              <span className="ml-2 rounded-full bg-primary/10 px-2 py-0.5 text-xs" data-testid="transfers-count">
                {transferCount}
              </span>
            )}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="reconciliations" className="mt-6">
          <ReconciliationList onChanged={refresh} />
        </TabsContent>
        <TabsContent value="transfers" className="mt-6">
          <TransferList onChanged={refresh} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
```

If the local shadcn `Tabs` primitive renders all `<TabsContent>` children regardless of active tab, the lazy-mount test will fail. **Verify before this commit:** if `<TabsContent>` is always rendered, replace the body with conditional rendering driven by `activeTab` (e.g. `{activeTab === 'reconciliations' && <ReconciliationList ... />}` and same for transfers, with the bare `<TabsContent>` slot used purely for the role/aria semantics).

- [ ] **Step 3: Run tests**

Run: `cd ProjectCeres.Client && pnpm test ReviewLayout`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReviewLayout.tsx \
        ProjectCeres.Client/src/app/features/review/ReviewLayout.test.tsx
git commit -m "feat(review): ReviewLayout shell with tabs, ?tab= sync, A3 default, lazy mount"
```

---

### Task 18: `ReviewLayout` first-paint flicker + user-touched tests

**Files:**
- Test: `ProjectCeres.Client/src/app/features/review/ReviewLayout.test.tsx`

These tests need to control the timing of when the count fetch resolves, so they are added separately rather than during Task 17.

- [ ] **Step 1: Append the two timing-sensitive tests**

```tsx
it('first-paint: shows Reconciliations while counts loading; switches to Transfers once counts resolve 0/N', async () => {
  let resolveRecon!: (v: Response) => void;
  let resolveXfer!:  (v: Response) => void;

  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return new Promise<Response>((r) => { resolveRecon = r; });
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return new Promise<Response>((r) => { resolveXfer = r; });
    }
    if (url.endsWith('/reconciliation-review/pending')) return Promise.resolve({ ok: true, json: async () => [] } as Response);
    if (url.endsWith('/transfer-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
    if (url.endsWith('/api/accounts'))                  return Promise.resolve({ ok: true, json: async () => [] } as Response);
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;

  render(
    <MemoryRouter initialEntries={['/review']}>
      <ReviewCountProvider>
        <Routes>
          <Route path="/review" element={<ReviewLayout />} />
        </Routes>
      </ReviewCountProvider>
    </MemoryRouter>,
  );
  // Initially Reconciliations is selected (provisional default while loading)
  expect(screen.getByRole('tab', { name: /Reconciliations/ })).toHaveAttribute('aria-selected', 'true');

  resolveRecon({ ok: true, json: async () => 0 } as Response);
  resolveXfer({ ok: true, json: async () => 3 } as Response);

  await waitFor(() => expect(screen.getByRole('tab', { name: /Transfers/ })).toHaveAttribute('aria-selected', 'true'));
});

it('first-paint + user-touched: post-load adjustment is suppressed when user already clicked', async () => {
  let resolveRecon!: (v: Response) => void;
  let resolveXfer!:  (v: Response) => void;

  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return new Promise<Response>((r) => { resolveRecon = r; });
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return new Promise<Response>((r) => { resolveXfer = r; });
    }
    if (url.endsWith('/reconciliation-review/pending')) return Promise.resolve({ ok: true, json: async () => [] } as Response);
    if (url.endsWith('/transfer-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
    if (url.endsWith('/api/accounts'))                  return Promise.resolve({ ok: true, json: async () => [] } as Response);
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;

  render(
    <MemoryRouter initialEntries={['/review']}>
      <ReviewCountProvider>
        <Routes>
          <Route path="/review" element={<ReviewLayout />} />
        </Routes>
      </ReviewCountProvider>
    </MemoryRouter>,
  );
  // User clicks Reconciliations explicitly (which is already selected, but the click sets userTouchedTab.current)
  await userEvent.click(screen.getByRole('tab', { name: /Reconciliations/ }));
  // Now counts arrive 0/N — would normally swing to Transfers; the user-touched flag must suppress that.
  resolveRecon({ ok: true, json: async () => 0 } as Response);
  resolveXfer({ ok: true, json: async () => 5 } as Response);
  await waitFor(() => {/* allow effect to flush */});
  expect(screen.getByRole('tab', { name: /Reconciliations/ })).toHaveAttribute('aria-selected', 'true');
});
```

- [ ] **Step 2: Run all `ReviewLayout` tests**

Run: `cd ProjectCeres.Client && pnpm test ReviewLayout`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/review/ReviewLayout.test.tsx
git commit -m "test(review): first-paint flicker + user-touched tab suppression"
```

---

### Task 19: Sidebar — wire the `Review` count badge

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/nav-items.ts`
- Modify: `ProjectCeres.Client/src/app/layout/Sidebar.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/Sidebar.test.tsx`

- [ ] **Step 1: Write the failing badge tests**

Add to `Sidebar.test.tsx` — the test must wrap `<Sidebar>` in `<ReviewCountProvider>` so the hook resolves:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { ReviewCountProvider } from '../features/review/ReviewCountProvider';
import { Sidebar } from './Sidebar';

function mockReviewCounts(reconciliation: number, transfer: number) {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) return Promise.resolve({ ok: true, json: async () => reconciliation } as Response);
    if (url.endsWith('/transfer-review/pending/count'))       return Promise.resolve({ ok: true, json: async () => transfer } as Response);
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;
}

describe('Sidebar — Review badge', () => {
  beforeEach(() => { vi.restoreAllMocks(); });

  it('renders Review badge when total > 0', async () => {
    mockReviewCounts(2, 1);
    render(
      <MemoryRouter>
        <ReviewCountProvider><Sidebar /></ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByTestId('nav-badge-review').textContent).toBe('3'));
  });

  it('hides Review badge when total === 0', async () => {
    mockReviewCounts(0, 0);
    render(
      <MemoryRouter>
        <ReviewCountProvider><Sidebar /></ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.queryByTestId('nav-badge-review')).toBeNull());
  });

  it('aria-label on Review link includes the count when present', async () => {
    mockReviewCounts(2, 1);
    render(
      <MemoryRouter>
        <ReviewCountProvider><Sidebar /></ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByRole('link', { name: /Review, 3 pending/ })).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Extend `NavItem` to support badges**

Edit `ProjectCeres.Client/src/app/layout/nav-items.ts`:

```tsx
import {
  BarChart3, Inbox, Landmark, LayoutList, LifeBuoy, Repeat,
  Settings as SettingsIcon, Tags, Upload, Wallet,
  type LucideIcon,
} from 'lucide-react';
import { useReviewCount } from '../features/review/ReviewCountProvider';

export type NavBadgeHook = () => number;

export type NavItem = {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Optional hook that returns the badge count. Render a pill when > 0. */
  useBadge?: NavBadgeHook;
};

export type NavGroup = { label: string; items: NavItem[]; };

const useReviewBadge: NavBadgeHook = () => useReviewCount().total;

export const navGroups: NavGroup[] = [
  {
    label: 'Activity',
    items: [
      { to: '/movements', label: 'Movements', icon: LayoutList },
      { to: '/review',    label: 'Review',    icon: Inbox, useBadge: useReviewBadge },
    ],
  },
  {
    label: 'Money',
    items: [
      { to: '/accounts',   label: 'Accounts',   icon: Landmark },
      { to: '/categories', label: 'Categories', icon: Tags },
      { to: '/budgets',    label: 'Budgets',    icon: Wallet },
    ],
  },
  {
    label: 'Tools',
    items: [
      { to: '/recurring', label: 'Recurring Transactions', icon: Repeat },
      { to: '/import',    label: 'Import',                  icon: Upload },
      { to: '/reports',   label: 'Reports',                 icon: BarChart3 },
    ],
  },
];

export const bottomItems: NavItem[] = [
  { to: '/settings', label: 'Settings', icon: SettingsIcon },
  { to: '/support',  label: 'Support',  icon: LifeBuoy },
];
```

- [ ] **Step 3: Render the badge inside `SidebarLink`**

Edit `ProjectCeres.Client/src/app/layout/Sidebar.tsx` — replace the existing `SidebarLink`:

```tsx
function SidebarLink({ item, collapsed }: { item: NavItem; collapsed: boolean }) {
  const badgeCount = item.useBadge?.() ?? 0;

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-3 rounded-md px-3 py-2 text-sm no-underline transition-colors',
      collapsed && 'justify-center px-2',
      isActive
        ? 'bg-accent text-accent-foreground shadow-[inset_2px_0_0_var(--primary)]'
        : 'text-muted-foreground hover:bg-muted hover:text-foreground',
    );

  const ariaLabel = badgeCount > 0 ? `${item.label}, ${badgeCount} pending` : item.label;
  const badge = badgeCount > 0 ? (
    <span
      data-testid={`nav-badge-${item.to.replace('/', '')}`}
      className="ml-auto rounded-full bg-primary/10 px-2 py-0.5 text-xs"
    >
      {badgeCount}
    </span>
  ) : null;

  const link = (
    <NavLink to={item.to} className={linkClass} end aria-label={ariaLabel}>
      <item.icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      {!collapsed && <span>{item.label}</span>}
      {!collapsed && badge}
    </NavLink>
  );

  if (!collapsed) return link;

  return (
    <Tooltip>
      <TooltipTrigger render={link} />
      <TooltipContent side="right">{ariaLabel}</TooltipContent>
    </Tooltip>
  );
}
```

The hook call inside `SidebarLink` is safe — `nav-items.ts`'s `useBadge` reference must call a hook unconditionally per item. Since the items array is static, every render of every `SidebarLink` calls the same set of hooks in the same order, satisfying React's Rules of Hooks. (If lint warns about "conditional hook," disable per-line with reasoning that the function reference is stable across renders.)

- [ ] **Step 4: Run Sidebar tests**

Run: `cd ProjectCeres.Client && pnpm test Sidebar`
Expected: all PASS (the new tests pass, existing Sidebar tests still pass).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/layout/nav-items.ts \
        ProjectCeres.Client/src/app/layout/Sidebar.tsx \
        ProjectCeres.Client/src/app/layout/Sidebar.test.tsx
git commit -m "feat(layout): Sidebar Review badge driven by ReviewCountProvider"
```

---

### Task 20: Wire the page into App, mount the provider, update placeholder Review test

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Review.tsx`
- Modify: `ProjectCeres.Client/src/App.tsx` (or `ProjectCeres.Client/src/app/App.tsx` — check the actual location)
- Modify: `ProjectCeres.Client/src/app/App.test.tsx`

- [ ] **Step 1: Re-export the layout from the page**

Edit `ProjectCeres.Client/src/app/pages/Review.tsx`:

```tsx
export { ReviewLayout as Review } from '../features/review/ReviewLayout';
```

- [ ] **Step 2: Mount `<ReviewCountProvider>` in the app shell**

Find the file that wraps the app routes (the one that already wraps `<ReminderCountProvider>`). Edit it to wrap `<ReviewCountProvider>` as a sibling:

```tsx
import { ReminderCountProvider } from './layout/ReminderCountProvider';
import { ReviewCountProvider } from './features/review/ReviewCountProvider';

// existing JSX, find the <ReminderCountProvider> block and wrap (or sibling) ReviewCountProvider:
<ReminderCountProvider>
  <ReviewCountProvider>
    {/* the existing app shell + Routes */}
  </ReviewCountProvider>
</ReminderCountProvider>
```

The exact placement depends on existing code — keep the provider above any consumer (Sidebar + ReviewLayout).

- [ ] **Step 3: Update the placeholder Review test**

Find the test that asserts the placeholder `h1 'Review'` (likely `App.test.tsx` or `pages/Review.test.tsx`). Update it to render the live page; given that the page now needs the provider + a fetch mock, the simplest assertion is:

```tsx
it('renders the Review page with both tabs', async () => {
  global.fetch = vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) return Promise.resolve({ ok: true, json: async () => 0 } as Response);
    if (url.endsWith('/transfer-review/pending/count'))       return Promise.resolve({ ok: true, json: async () => 0 } as Response);
    if (url.endsWith('/reconciliation-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch;

  render(
    <MemoryRouter initialEntries={['/review']}>
      <ReviewCountProvider>
        <Routes>
          <Route path="/review" element={<Review />} />
        </Routes>
      </ReviewCountProvider>
    </MemoryRouter>,
  );
  expect(await screen.findByRole('heading', { name: /^Review$/ })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: /Reconciliations/ })).toBeInTheDocument();
});
```

- [ ] **Step 4: Run the full client test suite to make sure nothing else regressed**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: green.

- [ ] **Step 5: Run the production build**

Run: `cd ProjectCeres.Client && pnpm build`
Expected: green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/Review.tsx \
        ProjectCeres.Client/src/App.tsx \
        ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres.Client/src/app/App.test.tsx
git commit -m "feat(review): mount ReviewCountProvider, wire /app/review to ReviewLayout"
```

(Add only the files that actually exist / were modified; the two `App.tsx` paths are listed in case the actual file is at one location or the other.)

---

### Task 21: Audit dropped throwing-method call sites once more

**Files:**
- (No edits — defensive audit per saved feedback `feedback_audit_cross_module_queries_on_cutover.md`)

- [ ] **Step 1: Grep for any remaining call sites**

```bash
grep -rn "ImportStagedTransactionService\|TransferReviewService" ProjectCeres ProjectCeres.Tests --include='*.cs' | \
  grep -E "\.ConfirmAsync|\.ConfirmAllAsync|\.DisputeAsync|\.LinkToExistingAsync|\.CreateAsTransferAsync|\.DismissAsTransactionAsync" | \
  grep -v "Try" | \
  grep -v "/Services/"
```
Expected: no matches outside `Services/` (the implementation file itself was already updated). If a Razor controller still references one, fix it now (either pre-apply the redirect or stub).

- [ ] **Step 2: Confirm `dotnet build` is green**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 3: No commit if nothing to change**

If the grep found nothing, this task is informational only — proceed to Task 22. If it found something, commit the fix as `chore(review): clean up final dropped-method call site`.

---

### Task 22: Razor cutover — slim controllers, delete views, drop sidebar badges, doc sync, final verification

This is the integration commit per the spec. All Razor cleanup + doc sync + final test pass land together so the cutover is atomic.

**Files:**
- Modify: `ProjectCeres/Controllers/TransferReviewController.cs`
- Modify: `ProjectCeres/Controllers/ReconciliationReviewController.cs`
- Modify: `ProjectCeres/Views/Shared/_Layout.cshtml`
- Delete: `ProjectCeres/Views/TransferReview/Index.cshtml`
- Delete: `ProjectCeres/Views/ReconciliationReview/Index.cshtml`
- Delete: `ProjectCeres/ViewModels/StagedTransferViewModel.cs`
- Delete: `ProjectCeres/ViewModels/StagedTransactionViewModel.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs` (if Razor-* tests present)
- Modify: `ProjectCeres.Tests/Integration/UiVerificationTests.cs` (if Razor view smoke tests present)
- Modify: `docs/planning-phase3-spa-migration.md`
- Modify: `docs/planning-phase3.md`
- Modify: `docs/api-contract.md`

- [ ] **Step 1: Slim `TransferReviewController` to redirects**

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class TransferReviewController : Controller
{
    public IActionResult Index()                                                       => Redirect("/app/review?tab=transfers");
    public IActionResult LinkToExisting(Guid stagedId, Guid otherAccountId)            => Redirect("/app/review?tab=transfers");
    public IActionResult CreateAsTransfer(Guid stagedId, Guid otherAccountId)          => Redirect("/app/review?tab=transfers");
    public IActionResult DismissAsTransaction(Guid stagedId)                           => Redirect("/app/review?tab=transfers");
}
```

- [ ] **Step 2: Slim `ReconciliationReviewController` to redirects**

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class ReconciliationReviewController : Controller
{
    public IActionResult Index()           => Redirect("/app/review?tab=reconciliations");
    public IActionResult Confirm(Guid id)  => Redirect("/app/review?tab=reconciliations");
    public IActionResult ConfirmAll()      => Redirect("/app/review?tab=reconciliations");
    public IActionResult Dispute(Guid id)  => Redirect("/app/review?tab=reconciliations");
}
```

- [ ] **Step 3: Drop the Razor sidebar badges for the two queues**

Edit `ProjectCeres/Views/Shared/_Layout.cshtml`:

- Remove the `@inject ProjectCeres.Services.ITransferReviewService TransferReviewService` line (and the equivalent `IImportStagedTransactionService` line if present).
- Remove the `await TransferReviewService.GetPendingCountAsync()` call (and the staged-transaction equivalent) and the badge markup that consumed them.
- If the file no longer references those service types, delete the corresponding `@using` entries.

- [ ] **Step 4: Delete the two Razor views and two Razor-only ViewModels**

```bash
git rm ProjectCeres/Views/TransferReview/Index.cshtml \
       ProjectCeres/Views/ReconciliationReview/Index.cshtml \
       ProjectCeres/ViewModels/StagedTransferViewModel.cs \
       ProjectCeres/ViewModels/StagedTransactionViewModel.cs
```

(Also remove the now-empty `ProjectCeres/Views/TransferReview/` and `ProjectCeres/Views/ReconciliationReview/` directories if git leaves them.)

- [ ] **Step 5: Drop residual Razor-era tests**

Search and delete:
- Any `RazorTransferReviewService_*_stamps_UserId` tests in `UserIdStampingTests.cs`.
- Any `RazorImportStagedTransactionService_*_stamps_UserId` tests in `UserIdStampingTests.cs`.
- Any `/TransferReview` or `/ReconciliationReview` view smoke tests in `UiVerificationTests.cs`.

```bash
grep -n "RazorTransferReviewService\|RazorImportStagedTransactionService\|/TransferReview\|/ReconciliationReview" \
  ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs \
  ProjectCeres.Tests/Integration/UiVerificationTests.cs
```

Delete only matches relevant to Review.

- [ ] **Step 6: Run the full server + client suites**

Run:
```bash
dotnet test
cd ProjectCeres.Client && pnpm test && pnpm build
```
Expected: all green.

- [ ] **Step 7: Sync docs (per saved feedback `feedback_sync_docs_before_spa_commits.md` and `feedback_changelog_verify_before_proposing.md`)**

For each doc target below, **first read the actual file** to confirm the line you intend to update is where you think it is, then make the edit.

In `docs/planning-phase3-spa-migration.md`:
- §2 controller table — flip both `TransferReviewController` and `ReconciliationReviewController` rows to "Migrated 2026-05-04" with the commit hash filled in after the commit lands (use `<sha>` placeholder for now and update post-commit, or use a follow-up commit per saved feedback if the placeholder bothers reviewers).
- §8 frontend execution batches table — flip the Review row from "Pending" to "✅ Migrated 2026-05-04 (commit `<sha>`)".

In `docs/planning-phase3.md` §14 — append:

```markdown
   - ✓ **Review — unified `/app/review` with Reconciliations + Transfers tabs, sidebar count badge, ReviewCountProvider** (2026-05-04). Reconciliations: inline `Confirm match` + `Dispute` row menu (AlertDialog) + `Confirm all` (AlertDialog). Transfers: three buttons per card (`Link to existing` / `Create transfer` / `Dismiss`); Link/Create open a shared dialog with a same-currency-filtered account picker; Dismiss fires immediately. Server-side: throwing CRUD variants dropped from `ITransferReviewService` and `IImportStagedTransactionService`; `TryConfirmAllAsync` added; `StagedTransactionDto` and `StagedTransferDto` enriched with `AccountCurrencyCode` + `AccountCurrencySymbol`. Razor controllers slimmed to redirects; views deleted. Spec: `docs/superpowers/specs/2026-05-04-review-spa-design.md`. Plan: `docs/superpowers/plans/2026-05-04-review-spa.md`.
```

In `docs/api-contract.md` — add an entry noting:
- `StagedTransactionDto` and `StagedTransferDto` now include `AccountCurrencyCode` (string) and `AccountCurrencySymbol` (string).
- `ITransferReviewService` and `IImportStagedTransactionService` throwing CRUD variants are removed; only `Try*` methods remain.

`docs/models.md` — no change.

- [ ] **Step 8: Final commit**

```bash
git add ProjectCeres/Controllers/TransferReviewController.cs \
        ProjectCeres/Controllers/ReconciliationReviewController.cs \
        ProjectCeres/Views/Shared/_Layout.cshtml \
        ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs \
        ProjectCeres.Tests/Integration/UiVerificationTests.cs \
        docs/planning-phase3-spa-migration.md \
        docs/planning-phase3.md \
        docs/api-contract.md
# git rm done in Step 4 already staged the deletions
git commit -m "$(cat <<'EOF'
feat(spa): cutover Razor Review controllers + doc sync

Slims TransferReviewController (4 actions) and ReconciliationReviewController
(4 actions) to 302 redirects targeting /app/review with the appropriate
?tab= param. Deletes the two Razor Index views and two Razor-only
ViewModels (StagedTransferViewModel, StagedTransactionViewModel). Drops
the Razor sidebar count badges that pointed at the now-redirect-only
controllers.

Doc sync: planning-phase3-spa-migration.md §2 controller table + §8
batches table, planning-phase3.md §14, api-contract.md (DTO enrichment +
service interface cleanup). models.md unchanged.

Spec: docs/superpowers/specs/2026-05-04-review-spa-design.md
Plan: docs/superpowers/plans/2026-05-04-review-spa.md
EOF
)"
```

- [ ] **Step 9: Manual click-through (user step)**

Per the spec §9 verification gate, walk the page in a browser:

1. Visit `/app/review`. Page renders with both tabs. Default tab matches the A3 logic.
2. Sidebar `Review` shows the combined count badge if total > 0; hidden if 0.
3. Click the `Reconciliations` tab. URL becomes `/app/review?tab=reconciliations`. Cards render. Each card shows account / date / amount / description / matched pill.
4. Click inline `Confirm match` on a row. Toast "Confirmed." Row disappears. Sidebar count + tab count drop by 1.
5. Click `⋯` on a different row → `Dispute`. AlertDialog opens with consequence copy. Click `Dispute match`. Toast "Disputed. Original un-cleared and a new transaction added." Row disappears. Counts drop.
6. Visit Movements. Verify the original transaction is no longer cleared. Verify a new transaction with `Needs review` flag exists for the disputed CSV row.
7. Click `Confirm all`. AlertDialog shows the captured count. Click confirm. Toast "Confirmed N matches." All rows disappear. Tab shows empty state.
8. Click `Transfers` tab. URL becomes `/app/review?tab=transfers`. Cards render. Cards with a candidate show the pill + `Link to existing` button; cards without a candidate omit both.
9. Click `Link to existing` on a card with a candidate. Dialog opens with title "Link to existing transfer." Picker shows only same-currency, active, non-self accounts. Pick one. Click `Link transfer`. Toast "Linked." Row disappears. Counts drop. Visit Movements; verify a Transfer record exists.
10. Click `Create transfer` on another card. Dialog opens with title "Create transfer." Pick an account. Click `Create transfer`. Toast "Transfer created." Row disappears.
11. Click `Dismiss` on a card. No dialog — fires immediately. Toast "Imported as a plain transaction." Row disappears. Visit Movements; verify the row landed as a plain Transaction.
12. Trigger a same-currency-only constraint case: stage a transfer on an EUR account, click `Link to existing`, verify USD accounts are absent from the picker.
13. Visit `/TransferReview`. 302 → `/app/review?tab=transfers`. Same for `/ReconciliationReview` → `/app/review?tab=reconciliations`. Same for the action POST URLs.
14. Visit `/app/review?tab=garbage`. Falls through to A3 default.
15. Toggle tabs while one queue is empty: empty tab still appears with no badge, content shows the empty state.
16. Mobile width (375px): cards stack, action buttons wrap onto a second row inside the card without overflow. Tab triggers fit.

If any step fails, file a follow-up commit; do not amend the cutover commit.

---

## Self-review

**1. Spec coverage:** every section of the spec maps to one or more tasks.

| Spec section | Implementing task(s) |
|---|---|
| §2 Goals — unified `/app/review` with tabs | Tasks 17 + 20 |
| §2 Goals — A3 default tab | Tasks 17 + 18 |
| §2 Goals — `?tab=` URL state | Task 17 |
| §2 Goals — Both tab triggers always visible | Task 17 |
| §2 Goals — flat `features/review/` | Tasks 7–18 |
| §2 Goals — Sidebar combined badge | Task 19 |
| §2 Goals — `ReviewCountProvider` | Tasks 8 + 9 + 20 |
| §2 Goals — Reconciliations: inline Confirm + ⋯ Dispute + Confirm-all dialog | Tasks 10 + 11 + 12 + 13 |
| §2 Goals — Transfers: three buttons + dialog picker + immediate Dismiss + hidden Link | Tasks 14 + 15 + 16 |
| §2 Goals — No trailing ellipsis | All copy in tasks 11/12/14/16 verified |
| §2 Goals — Drop throwing variants | Tasks 4 + 5 |
| §2 Goals — Add `TryConfirmAllAsync` | Tasks 1 + 2 |
| §2 Goals — Migrate `ConfirmAll` endpoint | Task 3 |
| §2 Goals — DTO enrichment | Task 6 |
| §2 Goals — Razor cutover | Task 22 |
| §9 Tests — all 80 client tests | Tasks 8/9/10/11/12/13/14/15/16/17/18/19 |
| §9 Tests — server `TryConfirmAllAsync` (5 tests) | Tasks 1 + 2 |
| §9 Tests — currency enrichment (2+2 tests) | Task 6 |
| §9 Verification gate | Task 22 |

**2. Placeholder scan:** searched for "TBD", "TODO", "FIXME", "implement later", "fill in details", "appropriate error handling", "edge cases", "Similar to Task" — none present. Two intentional notes for the implementer (the `TransferReviewController` stubbing choice in Task 4, the `AccountListItemDto` field-name verification in Task 15, the currency-mismatch domain code verification in Task 16, the shadcn `<TabsContent>` always-rendered verification in Task 17) are flagged inline with the verification step the implementer must run before writing the test or making the assumption.

**3. Type consistency:** spot-checked the names that travel across tasks:
- `ReviewCountProvider` shape `{ reconciliationCount, transferCount, total, loading, refresh }` — defined Task 8, consumed Tasks 17, 19, 20. Consistent.
- `StagedTransactionDto` / `StagedTransferDto` — defined Task 7, projected by API in Task 6, consumed Tasks 10, 14, 13, 15. Field names match.
- `AccountOption` exported from `TransferCard.tsx` Task 14, consumed by `TransferActionDialog.tsx` Task 16 + `TransferList.tsx` Task 15. Consistent.
- `onChanged` callback prop on every list/card/dialog — consistent contract: parent calls `provider.refresh()` + own `list.refetch()`.
- URL constants in `review-api.ts` Task 7 — used identically in every test fetch assertion.

No drift found.

---

**End of plan.**
