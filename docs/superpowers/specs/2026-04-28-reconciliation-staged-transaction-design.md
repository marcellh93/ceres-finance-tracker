# Reconciliation Staged Transaction — Design Spec

**Date:** 2026-04-28  
**Status:** Approved  
**Closes:** Roadmap Phase 2 checklist item — "Different transaction" reconciliation

---

## Problem

`ImportService.ImportAsync` auto-clears existing transactions that fuzzy-match a CSV row (same amount, date ±1 day). The matched CSV row is silently consumed — no record is kept. If the match was wrong, the user has no way to dispute it.

---

## Solution

Every reconciled row is staged as an `ImportStagedTransaction` record. A new `/Import/ReconciliationReview` screen lets the user confirm correct matches or dispute wrong ones. Disputing inserts the CSV row as a new transaction and un-clears the original.

---

## New Database Table

**`ImportStagedTransaction`**

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ImportedAt` | `DateTime` | UTC, set at import time |
| `AccountId` | `Guid` | FK → Account |
| `RawDate` | `DateOnly` | From CSV row |
| `RawAmount` | `decimal(18,2)` | Absolute value from CSV row |
| `RawDescription` | `string?` | From CSV row |
| `MatchedTransactionId` | `Guid` | FK → Transaction that was auto-cleared |
| `Status` | `varchar(20)` | `Pending` / `Confirmed` / `Disputed` |
| `ResolvedAt` | `DateTime?` | Nullable UTC |

No soft delete — resolved rows (Confirmed or Disputed) are kept for audit history but never shown on the review screen.

---

## Status Enum

```csharp
public enum StagedTransactionStatus
{
    Pending,
    Confirmed,
    Disputed
}
```

---

## Service Interface

```csharp
public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task ConfirmAsync(Guid id);
    Task ConfirmAllAsync();
    Task DisputeAsync(Guid id);
}
```

**`ConfirmAsync(id)`** — sets `Status = Confirmed`, `ResolvedAt = UtcNow`. No other changes (matched transaction stays cleared).

**`ConfirmAllAsync()`** — calls `ConfirmAsync` for all `Pending` rows in one pass.

**`DisputeAsync(id)`** — in a single transaction:
1. Set `Status = Disputed`, `ResolvedAt = UtcNow`
2. Call `ITransactionService.MarkClearedAsync(MatchedTransactionId, cleared: false)` — un-clears the original
3. Insert the CSV row as a new transaction via `ITransactionService.CreateAsync(...)` with `NeedsReview = true`

---

## ImportService Changes

In `ImportAsync`, when a reconciliation match is found, after calling `MarkClearedAsync`:

```csharp
db.ImportStagedTransactions.Add(new ImportStagedTransaction
{
    Id                    = Guid.NewGuid(),
    ImportedAt            = DateTime.UtcNow,
    AccountId             = accountId,
    RawDate               = row.Date,
    RawAmount             = Math.Abs(row.Amount),
    RawDescription        = row.Description,
    MatchedTransactionId  = match.Id,
    Status                = StagedTransactionStatus.Pending
});
```

No other changes to `ImportAsync` logic.

---

## Controller

**`ReconciliationReviewController`** with actions:

| Action | Method | Description |
|---|---|---|
| `Index` | GET | Lists all `Pending` staged transactions |
| `Confirm(Guid id)` | POST | Confirms single row |
| `ConfirmAll` | POST | Confirms all pending rows |
| `Dispute(Guid id)` | POST | Disputes single row |

All POST actions redirect to `Index` with `TempData` success/error message.

---

## View — `/Import/ReconciliationReview`

Same card-per-row layout as Transfer Review (`Views/TransferReview/Index.cshtml`).

Each card shows:
- Account name + import timestamp
- CSV row: date, amount, description
- Matched transaction: date, amount, description (with a "Matched to" label)
- Two buttons: **Confirm** (green, check icon) and **Dispute** (red, x icon)

Top of the page (when rows exist): **"Confirm All"** button (secondary style).

Empty state: "No pending reconciliations." with a link to Transactions.

---

## Import Summary Changes

The existing "Reconciled" tile becomes a link to `/Import/ReconciliationReview` when `RowsReconciled > 0`. No new tile — the existing blue tile is sufficient.

---

## Nav Indicator

Pending staged transaction count added to `_Layout.cshtml` alongside the existing staged transfer count. Same server-rendered badge pattern — no React, no async fetch.

---

## What Does NOT Change

- `TransferReviewController` / Transfer Review screen — untouched
- `ClearedBadge` React component — untouched
- `ImportService` detection/staging logic — untouched (only the reconciliation block gets the `.Add(...)` call)
- All existing tests — must pass without modification

---

## TDD Plan

1. Write integration tests for `ImportStagedTransactionService` (all fail first):
   - `GetPendingAsync` returns only `Pending` rows
   - `ConfirmAsync` sets `Status = Confirmed`, `ResolvedAt` set, matched transaction stays cleared
   - `ConfirmAllAsync` confirms all pending rows in one call
   - `DisputeAsync` sets `Status = Disputed`, un-clears original transaction, inserts new transaction with `NeedsReview = true`
2. Write integration test for `ImportService` — reconciled row creates an `ImportStagedTransaction` record (fails first)
3. Implement model, migration, service, controller, views
4. All new tests pass; all 349 existing tests still pass

---

## Files

### New
| File | Purpose |
|---|---|
| `ProjectCeres/Models/ImportStagedTransaction.cs` | Model |
| `ProjectCeres/Models/StagedTransactionStatus.cs` | Enum |
| `ProjectCeres/Services/IImportStagedTransactionService.cs` | Interface |
| `ProjectCeres/Services/ImportStagedTransactionService.cs` | Implementation |
| `ProjectCeres/Controllers/ReconciliationReviewController.cs` | Controller |
| `ProjectCeres/Views/ReconciliationReview/Index.cshtml` | Review screen |
| `ProjectCeres/ViewModels/StagedTransactionViewModel.cs` | View model |
| `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs` | Tests |

### Modified
| File | Change |
|---|---|
| `ProjectCeres/Data/AppDbContext.cs` | Add `DbSet<ImportStagedTransaction>` |
| `ProjectCeres/Services/ImportService.cs` | Stage reconciled rows |
| `ProjectCeres/Views/Import/Summary.cshtml` | Reconciled tile becomes a link |
| `ProjectCeres/Views/Shared/_Layout.cshtml` | Pending reconciliation count in nav |
| `ProjectCeres/Program.cs` | Register `IImportStagedTransactionService` |
