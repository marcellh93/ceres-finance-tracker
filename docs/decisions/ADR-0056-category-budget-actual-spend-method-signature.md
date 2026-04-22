# ADR 0056: CategoryBudget Actual Spend Method Signature

## Status: Accepted

## Context

`CategoryBudget` is a standing monthly spending cap — it has no date fields of its own.
`GetActualSpendAsync` must query the `Transactions` table for a matching category + currency
over some time window to produce the "spent so far" figure.

Two signature options were considered:

**Option A — hardcoded to current month**

```csharp
Task<decimal> GetActualSpendAsync(Guid categoryBudgetId);
```

Simple. Works for the dashboard. Breaks the moment any caller needs a historical month —
e.g. the Budget vs. Actual report (Stage 7), which must compare spent vs. limit across
multiple past months. Fixing it later requires changing the signature and every call site.

**Option B — caller-specified month**

```csharp
Task<decimal> GetActualSpendAsync(Guid categoryBudgetId, int year, int month);
```

The caller always specifies the month. The dashboard passes `DateTime.Today.Year` and
`DateTime.Today.Month`. The Budget vs. Actual report passes whichever month it is
iterating over. No signature change is needed when Stage 7 is built.

## Decision

**Option B.** `GetActualSpendAsync` accepts `year` and `month` parameters. No default
overload — callers always state the month explicitly.

The dashboard API endpoint calls the method with the current month. This is the default
behaviour in practice, but it is expressed at the call site, not hidden inside the service.

## Consequences

**Positive:**
- Budget vs. Actual report (Stage 7) can call the same method without a refactor
- The service has no hidden dependency on `DateTime.Today` — easier to test with any month
- Call sites are explicit about which month they are querying

**Negative:**
- Dashboard call site is two lines instead of one — minor and intentional
