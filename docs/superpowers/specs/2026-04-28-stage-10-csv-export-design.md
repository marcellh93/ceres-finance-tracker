# Stage 10 — CSV Export Design

**Date:** 2026-04-28
**Phase:** 2
**Roadmap reference:** Stage 10

---

## Overview

Add a "Export CSV" button to the Transactions Index page that downloads the current filtered view as a `.csv` file. Applies the same `accountId`, `from`, and `to` filters active on the page. Implements CSV injection prevention on all string fields.

---

## Architecture

Three new units, each with one responsibility:

### 1. `CsvFormattingHelper` (static class)

**File:** `ProjectCeres/Helpers/CsvFormattingHelper.cs`

Extracts the `Csv(string?)` sanitisation and `CsvFile(List<string>, string)` helpers from `ReportsController` into a shared static class. `ReportsController` is updated to call it — no behavior change.

```csharp
public static class CsvFormattingHelper
{
    public static string Csv(string? value) { ... }
    public static FileContentResult CsvFile(List<string> lines, string fileName) { ... }
}
```

### 2. `ITransactionExportService` / `TransactionExportService`

**Files:**
- `ProjectCeres/Services/ITransactionExportService.cs`
- `ProjectCeres/Services/TransactionExportService.cs`

One method: `ExportAsync(Guid? accountId, DateOnly? from, DateOnly? to)` returning `IReadOnlyList<TransactionExportRow>`.

Queries `Transaction` entities only (not liability payments — the export is the transaction ledger). Includes `Category` and `Account` navigation properties. Applies the same filter clauses as `TransactionService.GetAllAsync`. Returns a full result set — no pagination.

```csharp
public interface ITransactionExportService
{
    Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null);
}

public record TransactionExportRow(
    DateOnly Date,
    string Account,
    string Category,
    string CategoryType,
    string? Description,
    decimal Amount);
```

Registered in DI as scoped.

### 3. `TransactionsController.Export` action

**Route:** `GET /Transactions/Export?accountId=&from=&to=`

Accepts the same optional filter params as `Index`. Calls `ITransactionExportService.ExportAsync`, builds CSV lines using `CsvFormattingHelper.Csv()` for all string fields, streams the result via `CsvFormattingHelper.CsvFile()`.

**Filename:** `transactions_{yyyy-MM-dd}.csv` (no filter) or `transactions_{from}_{to}.csv` (when date range is applied).

---

## Data Flow

```
GET /Transactions/Export?accountId=&from=&to=
  → TransactionsController.Export
  → ITransactionExportService.ExportAsync(accountId, from, to)
  → EF query: Transactions.Include(Category).Include(Account)
               filtered by accountId / from / to
  → IReadOnlyList<TransactionExportRow>
  → controller builds CSV lines (CsvFormattingHelper.Csv() on all strings)
  → CsvFormattingHelper.CsvFile(lines, fileName)
  → FileContentResult (text/csv; charset=utf-8)
```

CSV columns (in order): `Date`, `Account`, `Category`, `Type`, `Description`, `Amount`

---

## Error Handling & Edge Cases

| Scenario | Behaviour |
|---|---|
| No transactions match filters | Returns CSV with header row only; 200 OK |
| `accountId` provided but not found | Service returns empty list; same as no-match |
| String field starts with `=`, `@`, `+`, `-` | Prefixed with `'` by `CsvFormattingHelper.Csv()` |
| String field contains comma, quote, or newline | Wrapped in double-quoted field by `CsvFormattingHelper.Csv()` |
| Null/empty string field | Returned as empty string |

---

## UI

- **Export CSV button** on `Views/Transactions/Index.cshtml` — Lucide `download` icon, positioned in the filter bar / page header area
- Button `href` constructed to pass current `accountId`, `from`, `to` query params — mirrors the active filter state

---

## Testing

### Unit tests — `ProjectCeres.Tests/Unit/CsvFormattingHelperTests.cs`

| Test | Assertion |
|---|---|
| Value starting with `=` | Returns `'=...` |
| Value starting with `@` | Returns `'@...` |
| Value starting with `+` | Returns `'+...` |
| Value starting with `-` | Returns `'-...` |
| Normal value | Returned unchanged |
| Value containing comma | Wrapped in double quotes |
| Null value | Returns empty string |

### Integration tests — `ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs`

| Test | Assertion |
|---|---|
| Seed 5 transactions, export no filter | 5 rows returned |
| Seed transactions across two accounts, filter by `accountId` | Only matching account's rows returned |
| Seed transactions with known date range, filter by `from`/`to` | Only in-range rows returned |

### Integration test — controller endpoint

| Test | Assertion |
|---|---|
| `GET /Transactions/Export` | 200 OK, `Content-Type: text/csv`, row count matches seed |

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `ProjectCeres/Helpers/CsvFormattingHelper.cs` | Shared CSV sanitisation + file result helper |
| `ProjectCeres/Services/ITransactionExportService.cs` | Export service interface |
| `ProjectCeres/Services/TransactionExportService.cs` | Export service implementation |
| `ProjectCeres.Tests/Unit/CsvFormattingHelperTests.cs` | Unit tests for CSV injection rules |
| `ProjectCeres.Tests/Integration/TransactionExportServiceTests.cs` | Integration tests for export filter logic |

### Modified files

| File | Change |
|---|---|
| `ProjectCeres/Controllers/ReportsController.cs` | Replace inline `Csv()` / `CsvFile()` with `CsvFormattingHelper` calls |
| `ProjectCeres/Controllers/TransactionsController.cs` | Add `Export` action |
| `ProjectCeres/Views/Transactions/Index.cshtml` | Add Export CSV button with Lucide `download` icon |
| `ProjectCeres/Program.cs` | Register `ITransactionExportService` → `TransactionExportService` in DI |
