# ADR 0040: Goal Budget Types and Single FK Limit

## Status: Accepted

## Context

Phase 2 introduces goal budgets — purpose-driven financial targets (e.g. "Trip to Japan —
€3,000", "Emergency Fund — €5,000"). Two design questions needed resolution before the
feature could be built:

**Question 1: Can a transaction link to more than one goal budget?**

`Transaction.BudgetId` is a single nullable FK. Splitting one payment across two budgets
(e.g. €500 split €300/€200 across two goals) would require replacing it with a
`TransactionBudgetContribution` junction table, affecting every budget report query, the
transaction create/edit views, and the import flow.

**Question 2: What counts as a contribution toward a goal budget?**

The initial assumption — only expense transactions count — breaks down for savings goals.
"Emergency Fund — €5,000" is not funded by expenses; it is funded by transfers to a
designated savings account. Forcing savings goals through expense transactions misrepresents
the data model and creates friction for a common use case.

## Decision

### Single FK limit kept

`Transaction.BudgetId` remains a single nullable FK. One transaction can be tagged to at
most one goal budget. The junction table is rejected — the complexity cost (schema change,
query impact, view changes, import impact) is disproportionate to the benefit for a scenario
most users will never encounter.

**Documented constraint:** a transaction can only contribute to one goal budget. If a
payment genuinely covers two goals, the user records two transactions — one per goal. This
is not a workaround; it reflects how most people think about goal contributions.

If Phase 2 daily use proves this limit genuinely painful, a `TransactionBudgetContribution`
junction table is the Phase 3 migration path.

### Two goal budget archetypes

A `GoalType` enum is added to `Budget` with two values: `Spending` and `Savings`.

**Spending goal** — tracks money spent toward a target.
- Progress source: sum of tagged expense transactions
- Example: "Trip to Japan — €3,000"
- Eligibility rules:
  - `Expense` transactions only — income transactions cannot be tagged
  - Transaction account currency must match budget currency (per ADR-0016)
  - Transaction date must fall within budget start/end date range
  - Budget must be active

**Savings goal** — tracks money accumulated in a designated account.
- Progress source: balance of `LinkedAccountId` account
- Example: "Emergency Fund — €5,000", "College Fund — €20,000"
- `LinkedAccountId` — nullable FK on `Budget` → `Account`, required for savings goals
- Linked account currency must match budget currency
- No transaction tagging — progress is always derived from the account balance
- Budget must be active

### Schema additions

```
Budget
  + GoalType       enum NOT NULL  ('Spending' | 'Savings')
  + LinkedAccountId uuid NULL FK → Account  (required when GoalType = 'Savings')
```

### Phase 2 feature scope

Both goal types share the same dashboard and report surface:

| Feature | Spending goal | Savings goal |
|---|---|---|
| Target amount | ✓ | ✓ |
| Progress (amount + %) | Sum of tagged expenses | LinkedAccount balance |
| Remaining amount | ✓ | ✓ |
| Projected completion date | Avg monthly spend × remaining | Avg monthly balance growth × remaining |
| Runway indicator (end date required) | ✓ | ✓ |
| Contribution/balance history | Tagged transaction list | Account balance history |
| Overspend display | Shown honestly, not capped | N/A — savings goals cannot overshoot |

### Milestones — deferred to Phase 3

Intermediate milestones within a goal budget (e.g. progress bar segments at €1,000 and
€2,000 toward a €3,000 goal) are deferred to Phase 3. Requires a `BudgetMilestone` table
and dedicated progress bar UI. The basic goal tracking loop must be validated in Phase 2
daily use before adding motivational layering.

## Consequences

**Positive:**
- Single FK keeps budget report queries simple — no junction table joins
- Two explicit goal types eliminate ambiguity about what counts as a contribution
- Savings goals derive progress from account balance — no workaround transactions needed
- Projected completion date and runway indicator are high-value, zero-schema-cost additions

**Negative:**
- One transaction per goal is a documented constraint — edge cases require two transactions
- `GoalType` and `LinkedAccountId` require a migration before the feature is built
- Savings goal progress is only as accurate as the linked account balance — if the account
  is used for purposes beyond the goal, the progress figure is overstated
