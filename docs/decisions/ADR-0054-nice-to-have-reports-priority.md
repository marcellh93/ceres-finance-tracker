# ADR 0054: Nice-to-Have Reports — Priority Order and Scope

## Status: Accepted

## Context

Six Phase 2 reports were listed with no build order and no decision on which are confirmed
in-scope vs. aspirational:

1. Net Worth Over Time
2. Monthly Cash Flow Trend
3. Spending by Category Over Time
4. Year-over-Year Comparison
5. Budget vs. Actual
6. Largest Expenses

A priority decision was needed before Phase 2 begins to avoid mid-phase scope creep and
to give the `ReportGeneratorFactory` (ADR-0036) a clear build sequence.

## Decision

### Confirmed Phase 2 reports (build in this order)

| Priority | Report | Rationale |
|---|---|---|
| 1 | **Budget vs. Actual** | Direct companion to the budgeting feature being built. Planned vs. actual spend per category. Near-zero extra query work on top of existing budget and transaction queries. |
| 2 | **Largest Expenses** | Simple `ORDER BY Amount DESC LIMIT N` query. High value for spotting outliers and one-off large purchases. Trivial to implement. |
| 3 | **Monthly Cash Flow Trend** | Income vs. expenses per month for a configurable period (default: last 6 months). Extends the existing Income & Expense Summary query with a time dimension. Feeds the dashboard Monthly Cash Flow chart directly. |
| 4 | **Net Worth Over Time** | Monthly equity snapshots over a configurable period. Requires calculating net worth at historical month-end points — slightly more complex but highly motivating for users tracking long-term progress. Feeds the dashboard Net Worth Over Time chart. |

### Deferred to Phase 3 — reassess at scope definition

| Report | Reason for deferral |
|---|---|
| **Spending by Category Over Time** | Overlaps significantly with Expense Breakdown + date range filter. Lower marginal value given existing reports. Defer if Phase 2 scope gets tight; reassess in Phase 3. |
| **Year-over-Year Comparison** | Requires at least two years of data to be meaningful. The app is unlikely to have sufficient history until well into Phase 2 daily use. Build in Phase 3 when data exists to validate it. |

## Consequences

**Positive:**
- Clear build sequence for `ReportGeneratorFactory` — one generator per report, added in
  priority order
- Budget vs. Actual lands alongside the budgeting feature — users immediately see the
  value of setting budgets
- Largest Expenses and Monthly Cash Flow Trend are low-effort, high-value additions
- Deferred reports are driven by data availability and usage evidence, not speculation

**Negative:**
- Spending by Category Over Time is a genuinely useful report — users who want month-by-month
  category breakdowns must use Expense Breakdown with manual date range changes in Phase 2
- Year-over-Year Comparison cannot be validated until sufficient history exists — this is
  a known Phase 2 limitation
