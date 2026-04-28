# ADR 0049: "Can I Afford This Right Now?" Financial Health Snapshot

## Status: Accepted

## Context

Personal finance apps consistently show what happened — transaction history, reports,
account balances. They rarely answer the question the user actually has: "am I okay right
now?" This is the most-cited pain point in personal finance app research.

A lightweight always-on dashboard panel can answer this question using data already in
the system. No new schema, no new entities, no reports to generate — purely arithmetic on
derived values `DashboardService` already computes or can easily extend.

## Decision

**Included in Phase 2.** A financial health snapshot panel lives on the dashboard,
always visible, requiring no user action to generate.

### Four metrics

**0. Spendable Balance (two-tier)**

How much can the user spend right now vs. after all planned obligations?

```
Available Today = liquid balance of non-excluded asset accounts
                − recurring bills due within the next 7 days (or overdue this calendar month)

Safe to Spend   = Available Today
                − recurring bills due 8–31 days from now (same calendar month)
                − budget reserve (SUM of MAX(0, limit − actual spend) per active CategoryBudget)
```

*Available Today* answers "can I buy this right now without missing a bill?"
*Safe to Spend* answers "can I buy this and stay on plan for the month?"

Both figures are displayed in the Financial Health card. *Available Today* is the headline. *Safe to Spend* is a smaller secondary line below it. A breakdown of deductions (bills due soon, bills later, budget reserve) is shown inline when any deduction is non-zero.

Edge cases:
- No qualifying asset accounts → both null (not shown)
- Safe to Spend < 0 → shown in amber, not red (it is a planning signal, not a crisis)
- Over-budget categories contribute zero to BudgetReserve (overspend is already reflected in the liquid balance)
- Known limitation: If a subscription has both a recurring entry and a CategoryBudget, it is subtracted twice from Safe to Spend (accepted in Phase 2).

**1. Runway**

How many months the user could sustain their current lifestyle if income stopped today.

```
Runway = (total assets − total liabilities) ÷ average monthly expenses
       = net worth ÷ average monthly expenses
```

Net worth is already a derived value. Average monthly expenses uses the last 6 months of
expense transactions. Displayed as:

> "You have **3.2 months** of runway."

Edge cases:
- Net worth is negative → runway displayed as 0, with a note that liabilities exceed assets
- Average monthly expenses is zero → runway not shown (no expense history yet)

**2. Income vs rolling baseline**

Is this month's income tracking above or below the user's recent average?

```
This month's income vs 6-month rolling average income
```

Displayed as:

> "This month you've earned **€1,800** — **€700 below** your 6-month average."
> or
> "This month you've earned **€3,200** — **€700 above** your 6-month average."

Edge cases:
- Less than 6 months of history → use available months, note the shorter window
- No income this month yet → show €0 vs baseline

**3. Budget burn rate**

Are spending patterns on track relative to how far through the month we are?

```
% of monthly budget spent ÷ % of month elapsed
```

Displayed as:

> "You're **60%** through the month and have spent **74%** of your budget."
> (implicit signal: spending is running ahead of pace)

Only shown when at least one active `CategoryBudget` exists. If no budgets are set,
this metric is hidden — not shown as zero.

### Implementation

All three metrics are added to `DashboardService` as new derived calculations. No new
service, no new entity, no migration required.

The panel is a Razor partial in Phase 2 — it renders server-side on page load alongside
the existing dashboard content. When React components are introduced to the dashboard,
this panel migrates to a React component backed by a `GET /api/dashboard/health` endpoint
in `Controllers/Api/`.

## Consequences

**Positive:**
- High value for the target user (freelancers) — answers "am I okay?" proactively
- Zero schema cost — all metrics derived from existing data
- `DashboardService` already exists and is tested — extending it is low risk
- Runway formula corrected to use net worth (assets − liabilities), not assets alone

**Negative:**
- Rolling average calculations require sufficient transaction history — new users will
  see incomplete or missing metrics until enough months accumulate. Handled via edge
  case display, not hidden errors.
- Budget burn rate is only meaningful when category budgets exist — shown conditionally
