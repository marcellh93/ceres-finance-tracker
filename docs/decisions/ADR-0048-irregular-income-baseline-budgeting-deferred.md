# ADR 0048: Irregular Income Baseline Budgeting — Deferred to Phase 3

## Status: Accepted

## Context

Standard monthly category budgets assume fixed income — a €300 Groceries cap is evaluated
the same way regardless of whether the user earned €800 or €4,000 that month. For
freelancers with variable income, this model produces false alarms in bad months and false
comfort in good ones.

The proposal was to evaluate category and goal budgets against a rolling average income
baseline (e.g. last 6 or 12 months) rather than a fixed monthly cap. Applied to the
50/30/20 framework, budget caps would float with the baseline:

```
Rolling 6-month average income: €2,500/month
  Needs cap:   €1,250  (50%)
  Wants cap:   €750    (30%)
  Savings cap: €500    (20%)
```

This is high value for the target user — freelancers are exactly the population where
fixed budgets break down.

Reasons for deferral:

- Requires a new budget evaluation model running alongside the existing fixed-amount model
- `CategoryBudget.LimitAmount` is a fixed euro value — the baseline approach requires
  either replacing this with a percentage or introducing a parallel percentage-based
  budget type
- Intersects with the 50/30/20 framework and `LifestyleTag` — but those currently work
  against fixed amounts, not a dynamic baseline
- Phase 2 already has significant scope; this risks being half-built if included now
- The fixed budget model must be validated in daily use before a dynamic layer is added
  on top of it

## Decision

**Deferred to Phase 3.** Not included in Phase 2.

This is a priority deferral — not an afterthought. The `LifestyleTag` groundwork is
already in place from Phase 1. The rolling baseline is the natural next layer once the
fixed budget model is proven correct in Phase 2 daily use.

Revisit at Phase 3 scope definition. The outcome may be implement, defer again, or
discard — driven by what Phase 2 usage reveals about how freelancers actually interact
with the fixed budget model.

## Consequences

**Positive:**
- Phase 2 budget model stays simple and focused — fixed amounts, clear evaluation
- `LifestyleTag` foundation is already in place for Phase 3 to build on
- No parallel evaluation models to maintain in Phase 2

**Negative:**
- Freelancers with highly variable income will find fixed budgets less useful in Phase 2
  — a known limitation, accepted as a Phase 2 constraint
