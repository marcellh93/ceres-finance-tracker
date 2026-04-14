# ADR 0031: Dedicated LiabilityPayment Entity Surfaced Through the Transactions UI

## Status: Accepted

## Context

Users need to record paying off a liability (e.g. a credit card payment from a checking account).
Before this change they had to enter two separate records: an expense on the asset account and
an income on the liability account. This was error-prone, required navigating to two separate
forms, and produced two unlinked entries with no semantic connection.

Three approaches were considered:

1. **Two regular transactions** — the status quo. No new code, but UX is poor and the two
   entries are not linked in any way. Income/expense reports are not distorted because the
   expense and income cancel out across different accounts — but the conceptual model is wrong:
   paying a credit card is not income and not a typical expense.

2. **Extend Transfer to cover asset → liability movements** — `Transfer` already handles
   inter-account movements. But its business rules assume both accounts are of the same type
   (same currency, neutral P&L effect). A liability payment has a different semantic: it reduces
   a debt and reduces an asset simultaneously. Mixing these into one entity would require
   type-discriminating logic inside `Transfer` and blur the clear semantics it currently has.

3. **New `LiabilityPayment` entity, surfaced through the existing Transactions UI** (chosen) —
   a separate entity preserves clean semantics and clear separation. Surfacing it through the
   Transactions UI rather than a separate page keeps the user experience unified: one place to
   record all money movements, one place to view history.

## Decision

A new `LiabilityPayment` entity is introduced. It stores: `AssetAccountId`, `LiabilityAccountId`,
`Amount`, `Date`, `Description`, `CreatedAt`. It has no category — it is excluded from all
income/expense report calculations, the same as `Transfer`.

The Transactions Create and Edit forms gain a type toggle at the top ("Transaction" /
"Liability Payment"). Toggling shows/hides the relevant fields (Category + Budget hidden for
payments; Liability Account shown instead). The Account label changes to "Paying From (Asset
Account)" when the payment type is selected. The toggle is driven by vanilla JavaScript — no
external library.

The Transactions Index list is refactored to use a unified `TransactionListItemViewModel`
read model. `TransactionService.GetRecentAsync` queries both `Transactions` and
`LiabilityPayments`, merges them in memory, sorts by date descending, and paginates.
`CountAsync` sums both tables.

`TransactionService` routes `CreateAsync`, `UpdateAsync`, and `DeleteAsync` to either
`ITransactionService`'s internal logic or `ILiabilityPaymentService` based on the
`TransactionType` discriminator on the ViewModel.

`AccountService.GetBalanceAsync` is extended to subtract liability payment amounts from
both the asset account and the liability account balance, in addition to the existing
`Transaction`-based balance calculation.

Business rules enforced by `LiabilityPaymentService`:
- Source must be an Asset account type
- Destination must be a Liability account type
- Both accounts must share the same currency
- Date must not precede the opening balance date of either account

## Consequences

**Positive:**
- One form, one history list — users never need to know a `LiabilityPayment` is a different
  entity under the hood
- Paying a liability is correctly excluded from income/expense totals (it is a balance-sheet
  event, not a P&L event)
- `Transfer` retains clean same-type semantics; no mixed-type logic added to it
- The two accounts involved are explicitly linked in one atomic record

**Negative:**
- `GetRecentAsync` performs two database queries and merges in memory instead of one
  database-side query. Acceptable for Phase 1 volumes. If the merged list grows large,
  this may need to be revisited with a union query or a view.
- The `TransactionType` discriminator is a stringly-typed string field ("Regular" /
  "LiabilityPayment"). An enum would be safer — deferred to avoid extra migration complexity
  for now.
- `LiabilityPayments` do not support file attachments. This is a Phase 2 concern if needed.
