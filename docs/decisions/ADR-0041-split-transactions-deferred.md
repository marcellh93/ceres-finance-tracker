# ADR 0041: Split Transactions Deferred — Revisit at Phase 3

## Status: Accepted

## Context

A split transaction allows one bank charge to be divided across multiple categories (e.g.
a €150 Costco purchase split as €90 Groceries, €40 Household, €20 Electronics). Supporting
this requires replacing the current 1:1 `Transaction→Category` relationship with a 1:many
`TransactionLine` table, where each line carries its own category and amount.

Phase 2 listed split transactions as optional. A go/no-go decision was required before
Phase 2 begins to avoid a mid-phase schema migration.

The cost of introducing `TransactionLine` in Phase 2:

- **Reports** — every category aggregation query changes from `SUM(t.Amount)` to
  `SUM(tl.Amount)` through a join. All existing report generators must be updated.
- **CSV import** — bank export rows are single-category; split import rows have no
  standard representation in a flat CSV format.
- **Transaction views** — create, edit, and detail views must handle a dynamic list of
  lines rather than a single amount + category pair.
- **Budget eligibility** — which line's amount counts toward a tagged goal budget requires
  a new rule.
- **TDD surface** — every service touching transactions needs new test cases covering the
  split scenario.

## Decision

**Split transactions are not included in Phase 2. The 1:1 `Transaction→Category` model
is kept.**

This is not a commitment to build split transactions in Phase 3. It is a deferral of the
decision — at Phase 3 scope definition, split transactions are reviewed again. The outcome
at that point may be: implement, defer again, or discard entirely depending on what Phase 2
daily use reveals about how often the limitation is actually felt.

Rationale for deferring now:
- Split transactions apply to a small minority of purchases — most transactions have one
  obvious category
- The schema change touches almost every part of the system disproportionately to the
  benefit at this stage
- The workaround — recording multiple transactions, one per category — is semantically
  correct and works cleanly with the existing model
- Phase 2 should validate the core transaction model in daily use before any decision to
  restructure it

**Documented constraint:** a transaction maps to exactly one category. If a single payment
covers multiple categories, the user records multiple transactions.

## Consequences

**Positive:**
- Report queries, import logic, and views remain simple through Phase 2
- No mid-phase migration risk
- TDD surface stays manageable

**Negative:**
- Users with frequent mixed-category purchases must record multiple transactions — a
  known friction point, accepted as a Phase 2 limitation
- If the feature is eventually built, the migration touches almost every part of the system
