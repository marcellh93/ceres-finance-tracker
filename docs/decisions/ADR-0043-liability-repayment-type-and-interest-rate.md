# ADR 0043: Liability Repayment Type and Interest Rate

## Status: Accepted

## Context

The liability payoff projection feature — "at my current payment pace, when is this debt
paid off?" — requires knowing how the liability is managed and optionally what interest
rate applies.

Not all liabilities behave the same way:

| Liability type | Interest | Payoff behavior |
|---|---|---|
| Mortgage / personal loan | Fixed rate | Long-term amortisation |
| Credit card (carried balance) | Variable rate | Revolving, interest accrues monthly |
| Credit card (paid in full each month) | None | Balance resets to zero monthly — no projection meaningful |
| Informal loan (friend, family) | None | Simple: balance ÷ monthly payment |

Showing a payoff projection panel on a credit card that is always paid in full is
misleading — the balance is a monthly obligation, not a debt being paid down over time.
A single `InterestRate` nullable field cannot express this distinction without additional
context about how the liability is managed.

## Decision

Two fields are added to `Account` for liability accounts:

### `LiabilityRepaymentType` enum

```csharp
public enum LiabilityRepaymentType
{
    FullMonthly,   // paid in full each month — no projection
    Amortising     // balance carried and paid down over time — projection shown
}
```

Present only on liability accounts. Asset accounts leave this field null.

### `InterestRate` nullable decimal

Optional. Only relevant for `Amortising` accounts. Null means no interest — projection
simplifies to `balance ÷ average monthly payment`.

### Schema additions

```
Account
  + LiabilityRepaymentType  varchar NULL  ('FullMonthly' | 'Amortising')
  + InterestRate             decimal NULL
```

Both fields are null for asset accounts. `LiabilityRepaymentType` is required when
creating a liability account. `InterestRate` is always optional.

### UX — account create/edit form

For liability accounts, the form includes:

> **How do you manage this liability?**
> ○ I pay it off in full each month
> ○ I carry a balance and pay it down over time
>   └── Interest rate (optional): [____] %

The interest rate field is shown only when "carry a balance" is selected.

### UI — account view per type

**`FullMonthly`**
- Current balance (amount owed this cycle)
- Due date (if a recurring transaction template exists)
- No projection panel

**`Amortising`**
- Current balance
- Projection panel:
  - Estimated payoff date
  - Total interest paid over remaining term (if `InterestRate` supplied)
  - Simplified projection — balance ÷ average monthly payment — if no rate supplied
- Interest rate shown and editable inline

## Consequences

**Positive:**
- UI adapts to the reality of each liability — no meaningless projection on a card paid
  in full
- `InterestRate` optional even for amortising accounts — useful projection even without it
- Low schema cost: two nullable columns, one migration
- Unlocks the liability payoff projection feature entirely in Phase 2

**Negative:**
- `LiabilityRepaymentType` must be set at account creation — adds one required field to
  the liability account form
- Projection arithmetic assumes a fixed average monthly payment — variable payment
  schedules (e.g. paying extra some months) will affect accuracy. Accepted as a Phase 2
  approximation.
