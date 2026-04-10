# ADR 0007: Financial Amounts Always Stored as Positive Values

## Status: Accepted

## Context
Financial transactions inherently have direction — money either flows in (income) or out
(expense). There are two common ways to model this:

1. **Signed amounts:** positive for income, negative for expenses. Direction is encoded in
   the number itself. Simple to sum, but requires sign-checking throughout the codebase
   and makes validation rules like "amount must be positive" ambiguous.

2. **Unsigned amounts with direction derived from context:** amounts are always positive;
   income vs. expense is determined by the transaction's category type. The sign is never
   stored — it is derived at query time.

Bank statement exports (CSV, OFX) commonly use signed amounts — debits are negative.
This creates a practical question for the Phase 2 import feature.

## Decision
All amount columns are always stored as positive values. Direction is never encoded in the
amount field. Instead, it is derived from the entity the amount belongs to:

- For `Transaction`: direction comes from `Category → CategoryType` (Income or Expense)
- For `Transfer`: no direction concept — transfers are neutral movements
- For `CategoryBudget.LimitAmount` and `Budget.TargetAmount`: always a positive target

When importing bank statements in Phase 2, the import logic is responsible for converting
negative debit values to positive amounts and mapping to an appropriate Expense category.
The raw sign from the bank file is discarded after mapping.

## Consequences

**Positive:**
- Amount validation is simple and unambiguous — reject any value ≤ 0
- No sign-handling logic scattered throughout the codebase
- Report queries sum amounts directly without conditional negation
- Consistent with how users think about money ("I spent €80" not "I spent −€80")

**Negative:**
- Import logic (Phase 2) must actively flip negative values and cannot pass bank data
  through without transformation — this logic must be tested carefully
- Developers unfamiliar with this convention may instinctively try to store negative values
  for expenses — the convention must be clearly communicated (hence this ADR)
- Querying "did this transaction increase or decrease this account's balance?" requires
  joining to Category and CategoryType rather than checking the sign of the amount
