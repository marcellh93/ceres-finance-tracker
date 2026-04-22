# ADR 0052: Liability Payoff Projection UI

## Status: Accepted

## Context

ADR-0043 established the schema groundwork for liability payoff projections:
`LiabilityRepaymentType` enum (`FullMonthly` | `Amortising`) and nullable `InterestRate`
on `Account`. The remaining decision was whether to build the projection panel UI in
Phase 2 and what it should show.

Every personal finance tool shows a debt balance. None show "at my current payment pace,
when is this paid off?" or "if I pay extra this month, how much do I save?" These are the
questions users actually have when looking at a liability account.

## Decision

**Liability payoff projection UI is included in Phase 2.**

The projection panel appears on the account view for `Amortising` liability accounts only.
`FullMonthly` accounts (credit cards paid in full) show no projection panel — per ADR-0043.

### Projection panel

```
Payoff projection
─────────────────────────────────────
Current balance:          €8,400
Avg monthly payment:      €350      (derived from last 6 months of transaction history)
Interest rate:            3.5%      (user-supplied — editable inline)
─────────────────────────────────────
Estimated payoff:         June 2028
Total interest remaining: €312

What if I pay extra each month?
[€ ________] → Payoff: January 2028 (5 months earlier · €89 interest saved)
```

### Calculation details

**Without interest rate (rate is null):**
```
Months remaining = current balance ÷ avg monthly payment
Payoff date = today + months remaining
```

**With interest rate:**
Standard amortisation formula applied to remaining balance, rate, and average payment.

**"What if" scenario:**
- User enters an extra monthly amount
- Projection recalculates payoff date and total interest saved
- In Phase 2 (no React yet): form POST recalculates server-side, panel re-renders
- In Phase 2 (once React is available): React component with client-side recalculation
  on input change, backed by `GET /api/accounts/{id}/projection?extraPayment=100`

### Edge cases

- Avg monthly payment is zero (no payment history yet) → projection not shown, message:
  "Record at least one payment to see your payoff projection."
- Interest rate not set → simplified projection shown with note: "Add an interest rate
  for a more accurate projection."
- Extra payment input exceeds remaining balance → payoff shown as next month

## Consequences

**Positive:**
- High-value feature for users managing loans and mortgages
- Schema is already in place — no migration needed
- "What if" scenario answers the question users actually ask when managing debt
- Graceful degradation when data is incomplete (no history, no rate)

**Negative:**
- Projection assumes a fixed average monthly payment — variable payment months affect
  accuracy. Accepted as a Phase 2 approximation.
- "What if" input requires either a form POST or React for interactivity — in early
  Phase 2 before React is introduced, the form POST approach adds a page reload
