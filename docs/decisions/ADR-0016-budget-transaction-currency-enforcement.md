# ADR 0016: Application-Level Enforcement of Budget/Transaction Currency Match

## Status: Accepted

## Context
`Transaction.BudgetId` is a nullable FK linking a transaction to a goal Budget. The Budget
has its own `CurrencyId`. A transaction's currency is not stored directly — it is inherited
from its Account (`transaction.Account.CurrencyId`).

Nothing in the schema prevents a transaction in EUR from being tagged to a Budget
denominated in USD. If this were allowed, the Budget's actual-spend calculation
(SUM of linked transaction amounts) would silently mix currencies, producing a meaningless
total with no error or warning.

Two enforcement options were considered:

1. **Database constraint:** A CHECK constraint or trigger that validates
   `transaction.Account.CurrencyId = budget.CurrencyId` at insert/update time.
   EF Core cannot express this as a simple FK or CHECK constraint — it spans three tables
   (Transaction → Account → Currency, Transaction → Budget → Currency). Requires a
   database trigger, which adds complexity outside the EF Core model and is harder to
   test and maintain.

2. **Application-level validation:** Check the currency match in the controller or service
   layer before saving. Reject the combination with a clear validation error if currencies
   differ. Standard EF Core practice for cross-entity business rules.

## Decision
Enforce at the application level. When a user tags a transaction to a budget, validate:

```
transaction.Account.CurrencyId == budget.CurrencyId
```

If this check fails, return a validation error and do not save. No database trigger or
cross-table constraint is added.

This check must be applied in two places:
- When creating a transaction with a BudgetId set
- When editing a transaction to change its BudgetId or its Account

## Consequences

**Positive:**
- No database triggers — all validation logic lives in the application layer, consistent
  with how other business rules are enforced (e.g. transfer currency match, CategoryBudget
  expense-only constraint)
- Clear user-facing validation error rather than a silent data integrity problem
- Easy to unit test in isolation

**Negative:**
- Relies on the application always being the entry point to data — direct database edits
  would bypass this check. Acceptable for a Phase 1/2 single-user local app.
- The validation requires eagerly loading `transaction.Account` and `budget` to compare
  their CurrencyIds — must not be skipped as a performance shortcut
