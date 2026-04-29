# Import Transfer Detection — Confidence Scoring Design

**Date:** 2026-04-29
**Status:** Approved

---

## Problem Statement

The current `TransferDetectionService` uses only **amount + date ±1 day** as the signal for cross-account transfer detection. This is too weak — it produces false positives when two unrelated transactions on different accounts share the same amount and date (e.g. €500 rent payment from checking and €500 client invoice received in savings on the same day).

When a false positive occurs, the import row is added to `ImportStagedTransfers` (status=Pending) and skipped from being imported as a transaction. The user must manually dismiss it from the Transfer Review screen. Until they do, the row is missing from their account entirely.

---

## Design

### Detection Logic

`TransferDetectionService.Detect` gains a fourth parameter — `IReadOnlyList<string> transferKeywords` — alongside the existing `exclusionPatterns`. The method runs the same two passes but the cross-account pass now splits its output into two confidence buckets.

**High-confidence** — staged in `ImportStagedTransfers`, skipped from import (existing behavior):
- Intra-file pair: two rows in the same CSV with opposite amounts and same date. Always high-confidence, unchanged.
- Cross-account match (amount + date ±1 day) **and** description contains a keyword from `transferKeywords`.

**Low-confidence** — imported normally as a transaction, suggestion created:
- Cross-account match (amount + date ±1 day) but **no** keyword match.

Low-confidence rows are **not** added to `RowIndicesToSkip`. They import as normal transactions with `NeedsReview = true`.

Keyword matching reuses the same case-insensitive substring logic as the existing exclusion pattern check (`IsExcluded`-style).

`ImportTransferExclusion` (description exclude-list) still applies before any detection pass, unchanged.

---

### Data Model

#### New entity: `TransferSuggestion`

One row per low-confidence import row. Lifecycle is independent from `ImportStagedTransfer`.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| Id | uuid | PK | |
| TransactionId | uuid | FK NOT NULL → Transaction (Cascade) | The imported transaction that triggered the suggestion |
| CandidateTransactionId | uuid | FK NULL → Transaction (SetNull) | The cross-account transaction it may pair with |
| CreatedAt | datetime | NOT NULL | When the suggestion was created during import |
| Status | varchar | NOT NULL DEFAULT 'Pending' | `Pending`, `Confirmed`, `Dismissed` |
| ResolvedAt | datetime | NULL | Set when status transitions out of Pending |

**Status lifecycle:**

| Status | Meaning |
|---|---|
| `Pending` | Badge visible on the transaction; user has not acted |
| `Confirmed` | User navigated to Transfer Review and confirmed the transfer |
| `Dismissed` | User dismissed the badge; row remains a plain transaction |

**Deletion:** Cascade-deleted with its `Transaction`. No soft-delete required.

**Expiry:** No `ExpiresAt` column on the row. Expiry is applied at query time using the user's `TransferSuggestionExpiryDays` setting:
```sql
WHERE Status = 'Pending' AND CreatedAt > NOW() - INTERVAL '{N} days'
```
If the setting is null, the date filter is omitted (never expire).

---

#### New entity: `ImportTransferKeyword`

Mirrors `ImportTransferExclusion`. Stores the include-list of description keywords that trigger high-confidence staging. Seeded with common ES/EN transfer terms; user can add or delete entries.

| Column | Type | Constraints | Notes |
|---|---|---|---|
| Id | uuid | PK | |
| Keyword | varchar | NOT NULL, unique index | Case-insensitive substring match |
| CreatedAt | datetime | NOT NULL | |
| IsSystemDefault | bool | NOT NULL DEFAULT false | System seeds flagged for UI distinction; still deletable by user |

**Deletion:** Hard delete.

**System default seeds (ES/EN):**
- ES: "transferencia", "traspaso", "envío", "bizum entre cuentas"
- EN: "transfer", "own transfer", "between accounts", "sepa credit transfer"

---

#### `Settings` addition

New column: `TransferSuggestionExpiryDays int? NULL`

- `NULL` = never expire (default)
- Any positive integer = suggestions older than N days are filtered out of active queries

No changes to `Transaction`, `ImportStagedTransfer`, or `ImportTransferExclusion`.

---

### Service Layer

#### `TransferDetectionService` / `ITransferDetectionService`

Updated signature:

```csharp
TransferDetectionResult Detect(
    IReadOnlyList<ParsedImportRow> rows,
    IReadOnlyList<Transaction> existingCrossAccountTxns,
    IReadOnlyList<string> exclusionPatterns,
    IReadOnlyList<string> transferKeywords,
    Guid accountId)
```

Updated result type:

```csharp
public record TransferDetectionResult(
    IReadOnlyList<ImportStagedTransfer> StagedRows,
    IReadOnlySet<int> RowIndicesToSkip,
    IReadOnlyList<SuggestionCandidate> SuggestionCandidates);

public record SuggestionCandidate(int RowIndex, Guid CandidateTransactionId);
```

`SuggestionCandidates` rows are absent from `RowIndicesToSkip`.

---

#### `ImportService.ImportAsync`

Three additions to the existing flow:

1. Load `transferKeywords` from `db.ImportTransferKeywords` alongside existing exclusion pattern load.
2. Pass `transferKeywords` into `Detect`.
3. After the transaction-creation loop, iterate `detection.SuggestionCandidates`: look up the `Transaction.Id` by `RowIndex` (collected during the loop as a `Dictionary<int, Guid>`), create a `TransferSuggestion` record for each, and save.

---

#### New: `ITransferSuggestionService` / `TransferSuggestionService`

Thin service for controller use:

```csharp
Task<IReadOnlyList<TransferSuggestion>> GetPendingAsync(int? expiryDays);
Task ConfirmAsync(Guid suggestionId);   // marks Confirmed; caller redirects to Transfer Review
Task DismissAsync(Guid suggestionId);  // marks Dismissed
```

---

### UI

#### Transaction list badge

Transactions with a `Pending` `TransferSuggestion` display a small dismissible badge — "Possible transfer" — adjacent to the description. The badge:
- Is a link to the Transfer Review screen with `CandidateTransactionId` pre-filled as the counterpart.
- Has a "×" dismiss button that POSTs to `DismissAsync` inline, no navigation.

The transaction list query left-joins `TransferSuggestion` (filtered by expiry) so badge data is fetched in one query, not N+1.

#### Settings screen

A new "Transfer Suggestions" section exposes:
- `TransferSuggestionExpiryDays` — number input (days); blank = never expire.
- `ImportTransferKeyword` list — add/remove keywords. System defaults are visually distinguished (e.g. "system" label) but remain deletable.

Mirrors the existing `ImportTransferExclusion` settings UI pattern.

#### Transfer Review screen

No changes. The confirm path redirects to it with the candidate pre-filled via existing query parameters.

---

### What Stays the Same

- High-confidence staging path: `ImportStagedTransfers` flow is unchanged.
- Intra-file pairing: always high-confidence, always staged.
- `ImportTransferExclusion` exclude-list: applied before any detection, unchanged.
- `Transaction` model: no new columns.
