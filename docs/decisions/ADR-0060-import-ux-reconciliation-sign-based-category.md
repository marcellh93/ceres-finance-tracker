# ADR 0060: Import UX Overhaul — Stepped Form, Reconciliation Pass, Sign-Based Category Fallback

## Status: Accepted

## Context

The original import form presented all fields simultaneously (file, account, category, column
mapping, flip-sign checkbox) with no progressive disclosure. Users with no saved profiles saw
a wall of dropdowns before uploading anything. The form also required a default category
selection — a poor fit for bank exports that mix income and expenses in a single file.

Three UX and behaviour problems were identified and addressed together:

1. **Form UX** — all fields visible at once; no guidance through the process; native checkbox
   was visually inconsistent.
2. **Default category** — forcing a single category for the entire import meant income rows
   and expense rows would be miscategorised by definition.
3. **Reconciliation** — existing transactions cleared via `IsCleared = true` after being
   matched, but a matching row would still be inserted as a duplicate. The reconciliation
   pass was not actually preventing duplicate creation.

## Decision

### Stepped import form

The form is split into three cards, each revealed only after the previous step is confirmed:

- **Step 1** — File + Account. A "Continue" button (not automatic reveal) triggers the
  header detection fetch and shows Step 2.
- **Step 2** — Column mapping pre-populated from `IHeaderDetectionService`. Saved profile
  selector shown only if profiles exist. Flip-sign toggle styled as a CSS toggle switch
  (Tailwind `peer`/`peer-checked:` pattern). A "Continue" button validates required columns
  and shows Step 3.
- **Step 3** — Review summary (file name, account, mapped columns). Import button submits
  the form.

The "Continue" button in Step 1 triggers the `/api/import/headers` fetch — the user opts
in rather than the page reacting automatically to a file selection event.

### Sign-based category fallback

The `CategoryId` field is removed from the import form entirely. Category is inferred per
row from the amount sign:

- Positive amount → `Uncategorized Income`  (GUID `20000000-0000-0000-0000-000000000025`)
- Negative amount → `Uncategorized Expense` (GUID `20000000-0000-0000-0000-000000000026`)

Both categories are seeded with `IsSystem = false` so they appear in transaction lists,
dropdowns, and balance calculations. `CategoryService.UpdateAsync` and `DeactivateAsync`
guard them via a GUID-based `IsReserved()` check — they cannot be renamed or deactivated
despite not having `IsSystem = true`.

`NeedsReview = true` is set on every newly inserted import row so the user can find and
recategorise them from the transaction list.

### Reconciliation pass — match then skip

Before inserting a row as a new transaction, `ImportService` checks for an existing
uncleared transaction in the same account matching:

- `existingTx.Amount == Math.Abs(row.Amount)` — stored amounts are always positive; CSV
  row amounts may be negative
- `Math.Abs(date difference) <= 1 day` — ±1 day tolerance for bank posting date variation

If a match is found, the existing transaction is marked `IsCleared = true` and the row is
**not** inserted. The result is counted as `RowsReconciled` (not `RowsImported`). This is
a merge-not-duplicate approach aligned with ADR-0039.

### Post-import save-profile prompt

If the user mapped columns manually (no saved profile used), the Summary screen offers a
name-and-save form so the mapping becomes a reusable profile. If a saved profile was used,
the prompt is suppressed.

### Summary screen

Four count cards replace the previous two:

| Card | Colour | Meaning |
|---|---|---|
| Imported | Green | New transactions inserted |
| Reconciled | Blue | Existing transactions cleared (no new row) |
| Needs Review | Yellow | Imported rows with `NeedsReview = true` |
| Failed | Red / Gray | Rows that could not be parsed |

## Consequences

**Positive:**
- Progressive disclosure reduces cognitive load — users see only what they need at each step
- No forced category selection — every import works for mixed-sign files
- Reconciliation pass prevents duplicates without requiring the user to do manual deduplication
- `NeedsReview` flag creates a natural categorisation queue

**Negative:**
- Three-step flow adds navigation steps vs. a single-submit form (accepted: steps are short)
- Uncategorized categories must be permanently protected via GUID guards, not the `IsSystem`
  flag — any future developer adding a query that filters by `IsSystem` may inadvertently
  expose them without realising the protection is in the service layer, not the flag
- `RowsReconciled` count is accurate only if the existing transaction was uncleared at import
  time — a re-import of a file where all rows were already cleared will report 0 reconciled
  even though the rows were legitimate matches
