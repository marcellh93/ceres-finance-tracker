# ADR-0061 — Import Transfer Staging (Plan B)

> ⏸️ **On hold (2026-06-29):** import is shelved from the Phase 3 beta — see [ADR-0078](ADR-0078-import-shelved-from-phase-3-beta.md). Paused, not superseded; resumes if import is un-shelved.

**Status:** Accepted — implemented 2026-04-26

---

## Context

The CSV/XLSX import pipeline (Stage 3.1–3.4) imports every row as a plain transaction after a reconciliation pass. Rows that are actually inter-account transfers imported this way create a data quality problem: the user ends up with duplicate expense/income entries that cancel out on the same-account ledger, but the other side of the transfer never appears as a proper `Transfer` record.

The problem is hard to detect at import time with certainty — the importer cannot know whether a €500 debit on 2026-01-15 is a grocery run or a transfer to a savings account. Manual review is required.

---

## Decision

Add a **transfer detection and staging** layer that runs before the reconciliation/insert pass during `ImportService.ImportAsync`. Detected rows are saved to a new `ImportStagedTransfer` table and excluded from the normal insert loop. A dedicated `/TransferReview` screen lets the user resolve each staged row with one of three actions.

### Detection logic (two-pass, stateless service)

`ITransferDetectionService.Detect(rows, crossAccountTxns, exclusionPatterns, accountId)` is pure — receives all inputs as parameters, does no DB access.

**Pass 1 — Intra-file pairing:**
If two rows in the same file have opposite signs and the same absolute amount on the same date, both are flagged as a suspected transfer pair.

**Pass 2 — Cross-account pairing:**
For each row, check whether any existing transaction in a different Ceres account has the opposite sign, the same absolute amount, and a date within `DateToleranceDays = 1`.

**Exclusion check:**
If the row's description contains any `ImportTransferExclusion.DescriptionPattern` (case-insensitive substring match), the row is excluded from staging and imported normally. This prevents re-staging rows the user has previously dismissed.

### New schema

**`ImportStagedTransfer`** — one row per staged import row. Transitions through `StagedTransferStatus` enum: `Pending → Linked | CreatedAsTransfer | DismissedAsTransaction`. FK to `Account` (Restrict — staged rows cannot outlive their account). FK to `CandidateTransaction` (SetNull — staged rows survive if the candidate is deleted). Never hard-deleted.

**`ImportTransferExclusion`** — one row per dismissed description pattern. Unique index on `DescriptionPattern`. Populated automatically when the user dismisses with "Not a Transfer". Hard-deleteable.

### Resolve actions (TransferReviewService)

| Action | Precondition | Effect |
|---|---|---|
| `LinkToExistingAsync(stagedId, otherAccountId)` | `CandidateTransactionId` set | Creates `Transfer`; status → `Linked` |
| `CreateAsTransferAsync(stagedId, otherAccountId)` | Any | Creates `Transfer`; status → `CreatedAsTransfer` |
| `DismissAsTransactionAsync(stagedId)` | Any | Creates plain `Transaction` (NeedsReview=true); saves exclusion pattern; status → `DismissedAsTransaction` |

**Transfer direction:** `RawAmount < 0` → staged account is SOURCE; other account is DEST.

### UI surfaces

- **`/TransferReview` (TransferReviewController):** Lists all `Pending` staged rows. Empty state when none. Per-row card with conditional "Link to existing" form (only when `CandidateTransactionId` is set).
- **Import Summary:** A fifth tile (`RowsStaged`) with warning color and link to `/TransferReview` when `RowsStaged > 0`.
- **Navbar badge:** `data-pending-transfers` attribute on `#navbar-root` read by the React navbar component; shows a destructive `Badge` when count > 0. Count served by `ITransferReviewService.GetPendingCountAsync()` injected into `_Layout.cshtml`.

---

## Alternatives Rejected

**Auto-create transfers on detection:** Requires knowing the other account with certainty. Neither intra-file nor cross-account pairing can provide that certainty — both rely on heuristics. A false positive would silently create a wrong transfer. Manual review is required.

**Show detected rows in the import form step 3:** Adds friction on every import even when no transfers are present. The staging screen is opt-in — the user visits it only when the import produced staged rows.

**Detect during parser, not ImportService:** The parser is format-blind and returns raw rows. Account context, existing transactions, and exclusion patterns are not available to the parser layer.

---

## Consequences

- Two new DB tables and a migration (`AddTransferStagingTables`).
- `ImportResult` gains `RowsStaged`. `ImportSummaryViewModel` gains `RowsStaged`. Both are backward compatible (default = 0).
- `ImportService` constructor gains optional `ITransferDetectionService` parameter — existing tests that construct `ImportService` directly without the parameter continue to work unchanged (detection is skipped when the parameter is null).
- `_Layout.cshtml` now injects `ITransferReviewService` alongside the existing `IRecurringTransactionService` to serve the nav badge count.
- Reconciliation ambiguous-match staging (staging rows that could be reconciliation matches) is **not implemented** — deferred. The current detection only stages rows that look like transfers, not rows that are ambiguous reconciliation candidates. This gap is acknowledged.
