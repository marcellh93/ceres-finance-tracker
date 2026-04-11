# ADR-0023 — Deletion Strategy: Three-Tier Model

**Status:** Accepted

**Context:**

Entities in the system have different relationships with historical data. A single deletion policy (hard delete everything, or soft delete everything) does not fit all cases:

- Some entities are referenced by transaction history. Hard-deleting them would orphan financial records and silently corrupt past reports and balances.
- Some entities have no downstream dependents and can be safely removed.
- Some entities have no financial history attached but are user-created records where accidental deletion should be recoverable.
- Some entities are system-defined reference data that must always exist for the app to function.

## Decision

Apply a three-tier deletion model based on each entity's relationship to financial history:

### Tier 1 — Deactivate (`IsActive = false`)

Applied to: `Account`, `Category`, `CategoryBudget`, `Budget`

These entities have transaction history attached. Hard delete would orphan records. Deactivation hides the entity from active pickers and lists but preserves it in all historical queries and aggregations. A deactivated account still contributes to net worth. A deactivated category still appears correctly in past reports.

System categories (`IsSystem = true`) cannot be deactivated — they are required for application logic (e.g. the Opening Balance category).

### Tier 2 — Soft Delete (`DeletedAt` timestamp)

Applied to: `SavedReport`

No financial history is attached to a saved report configuration, but the user may have deleted it accidentally. A `DeletedAt` timestamp records when it was removed and allows it to be restored on request. It is fully hidden once deleted but not permanently gone.

**Distinction from deactivation:** Deactivation means the record is still in active use by historical data and must remain in calculations. Soft delete means the user intentionally removed it and it has no ongoing role in history — it is restorable but not actively referenced.

### Tier 3 — Hard Delete with confirmation prompt

Applied to: `Transaction`, `Transfer`, `TransactionAttachment`

No downstream records depend on these entities. Deleting a transaction removes its contribution from any linked budget's actual spend, which is recalculated at query time. A confirmation prompt is shown before any hard delete to prevent accidental removal.

### Not deletable

`AccountType`, `CategoryType`, `ReportType`, `Currency` are system-defined reference data. Removing them would break the entities that reference them. They are seeded at startup and are never deletable.

`Settings` always has exactly one row in Phase 1. It is editable but not deletable.

## Consequences

**Positive:**
- Transaction and report history is never orphaned regardless of account or category lifecycle
- SavedReport supports recovery from accidental deletion
- Hard delete is available where it is safe — no accumulation of zombie records for transactions

**Negative:**
- Deactivated entities accumulate over time — all queries on active entities must include `WHERE IsActive = true`
- The three tiers require developers to check the deletion rule per entity before writing any delete operation; this must be enforced by convention and code review

**Related decisions:**
- ADR-0011: system categories carry an `IsSystem` flag and cannot be deactivated
- ADR-0024: derived values (balance, net worth) are computed at query time, so deactivated accounts are included in calculations without any special aggregation logic
