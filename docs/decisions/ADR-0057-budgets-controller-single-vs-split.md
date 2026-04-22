# ADR 0057: BudgetsController — Single Controller for Category and Goal Budgets

## Status: Accepted

## Context

Phase 2 introduces two distinct budget entities:

- `CategoryBudget` — monthly spending caps on expense categories
- `Budget` (goal budgets) — purpose-driven financial targets with `Spending` and `Savings`
  archetypes

Both need a standard CRUD surface: Index, Create, Edit, Deactivate. Two structural options
were considered for where these actions live.

**Option A — single `BudgetsController`**

One controller with two action groups (~12 actions total). Both entities share a "Budgets"
umbrella in the nav. The roadmap explicitly names `BudgetsController`.

**Option B — two controllers**

`CategoryBudgetsController` and `GoalBudgetsController` as separate files from the start.
Cleaner per-file SRP, but adds a file and a nav grouping decision before there is evidence
the single controller is painful.

## Decision

**Option A — single `BudgetsController`** for Phase 2.

SOLID compliance is maintained by keeping all business logic in the service layer
(`CategoryBudgetService`, `BudgetService`). The controller is a thin HTTP handler:
validate, call service, redirect or return view. If the controller grows unwieldy — or if
Phase 3 routing requirements diverge — splitting into two controllers is a localised
refactor that touches only the routing layer, not any logic.

## Refactor trigger

Split into `CategoryBudgetsController` + `GoalBudgetsController` if any of the following
occur:

- The controller exceeds ~16 actions
- Phase 3 introduces per-entity authorization or middleware that differs between the two
- A new developer consistently has to search to find which action group they need

## Consequences

**Positive:**
- Matches the roadmap's explicit naming — no doc drift
- Fewer files at a stage where both entities are small and parallel in structure
- Split is cheap when it becomes warranted — logic lives in services, not the controller

**Negative:**
- One controller carries two responsibilities at the class level — acceptable given thin
  actions, documented here as a known trade-off
