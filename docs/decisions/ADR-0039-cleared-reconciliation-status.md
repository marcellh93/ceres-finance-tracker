# ADR 0039: Cleared/Reconciliation Status and Duplicate Detection

## Status: Accepted

## Context

Phase 2 introduces CSV import. Without a duplicate detection strategy, importing a CSV
after manually entering the same transactions produces silent duplicates — a serious data
integrity problem for a financial app.

Two mechanisms were considered for duplicate detection:

- **Silent auto-rejection** — fingerprint match found, row silently skipped. Simple but
  opaque; the user never knows a row was dropped.
- **Cleared status + user review** — transactions carry an `IsCleared` flag. Potential
  duplicates are surfaced to the user for a conscious decision rather than resolved silently.

The second approach was chosen because financial data errors are hard to spot and hard to
undo. Nothing is resolved without user awareness.

A secondary question was whether reconciliation should use **delete-and-replace** (imported
row survives, manually entered transaction deleted) or **merge** (manually entered
transaction survives, imported row discarded). Delete-and-replace was rejected because
manually entered transactions may carry foreign key references — budget tags, category
assignments, and file attachments — that would be lost or orphaned on hard delete.

## Decision

### Schema

`IsCleared bool NOT NULL DEFAULT false` added to both `Transaction` and `Transfer`.

- Manually entered transactions and transfers always start `IsCleared = false`
- The cleared flag is set only after explicit verification — by the import flow or by the
  user directly

### Import clearing rules

| Scenario | Imported row state | Outcome |
|---|---|---|
| No potential duplicate found | `IsCleared = true` | Inserted and cleared automatically |
| Potential duplicate found | `IsCleared = false` | Inserted uncleared, flagged for review |
| User confirms match | Imported row discarded | Manually entered transaction marked `IsCleared = true`; all FKs preserved |
| User confirms different | Both survive | Imported row auto-marked `IsCleared = true` — no manual follow-up step |

### Merge-not-delete

When the user confirms a match between an imported row and a manually entered transaction,
the manually entered transaction is the record that survives. The imported row is discarded.

Rationale: the manually entered transaction may carry:
- `BudgetId` — a goal budget contribution that would be lost if the transaction were deleted
- `TransactionAttachment` records — receipts or invoices attached by the user
- A user-assigned `CategoryId` that the import inferred differently

Preserving the manually entered transaction preserves all of these. The only field updated
is `IsCleared = true`. Optionally, the date may be corrected to the bank date if the user
chooses.

### "Different transactions" auto-clears

When the user reviews a flagged pair and confirms the two entries are genuinely different
real-world payments, the imported row is automatically marked `IsCleared = true`. The user
has reviewed the row and confirmed it is real — that review is itself the verification.
No manual follow-up step is required.

### Fingerprint matching

Potential duplicates are identified by matching on:

```
date (±1 day tolerance) + amount + description + account
```

A match within this fingerprint is a *potential* duplicate — never auto-resolved. The ±1
day tolerance accounts for transactions manually entered the day after they occurred.

Fingerprint accuracy is evaluated during Phase 2 daily use. If false positives (legitimate
transactions incorrectly flagged) are frequent, the tolerance or the field combination is
adjusted. This is an explicit fine-tuning point, not a fixed contract.

### Reconciliation view

A dedicated screen lists all imported transactions currently in the
`IsCleared = false` + flagged state, with the potential matching transaction shown
alongside for comparison. The user acts on each flagged pair — match or different — before
it leaves the reconciliation queue.

## Consequences

**Positive:**
- No financial data is silently dropped or silently duplicated
- Manually entered transactions retain all FK references (budgets, attachments) through
  reconciliation
- "Different" confirmation auto-clears without a second manual step — minimal friction
- Fingerprint accuracy can be tuned incrementally based on real import data

**Negative:**
- `IsCleared` column added to both `Transaction` and `Transfer` — requires a migration
  before the import feature is built
- Reconciliation view is a new UI surface that must be designed and built
- Fingerprint matching on description is fragile if banks change their export format —
  known limitation, accepted as a Phase 2 starting point
