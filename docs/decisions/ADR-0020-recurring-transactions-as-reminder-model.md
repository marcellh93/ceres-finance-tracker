# ADR-0020 — Recurring Transactions: Reminder-Based Model, Not Auto-Creation

**Status:** Accepted (Phase 1)

**Context:**

Recurring financial events (rent, salary, subscriptions) are predictable in that they happen on a regular schedule, but unpredictable in the details:

- Amounts vary: utility bills change month to month, freelance income differs by project, even fixed expenses can have one-off adjustments
- Dates shift: payments can be delayed by holidays, bank processing windows, or late invoices
- Some periods are skipped or cancelled entirely: a subscription is paused, a client doesn't pay on time

Auto-creating a transaction on the due date would silently produce ledger entries with wrong amounts or wrong dates, and would create entries for events that did not actually occur. In a financial ledger, a record is meant to represent something that happened — not something that was scheduled to happen.

**Decision:**

Recurring transactions are modelled as a template (`RecurringTransaction` entity) that drives reminders, not automatic record creation.

- The template stores: name, estimated amount, account, category, frequency, day-of-period, and `NextDueDate`
- When `NextDueDate` is reached (within a configurable look-ahead window), a reminder surfaces on the dashboard — it does not create a `Transaction` record
- The user opens the reminder to see a pre-filled transaction form using the template values; they adjust any field if needed, then confirm to write a real `Transaction`
- On confirmation, `NextDueDate` advances to the next period
- Dismissing a reminder does not create a transaction; `NextDueDate` does not advance (the reminder stays pending for the user to handle later — e.g., they paid in cash and will record it manually)
- The estimated amount is a hint, not enforced — the user can change it before confirming

**Consequences:**

- The ledger only contains records for events the user has explicitly confirmed — no silent wrong-amount or wrong-date entries
- Users remain in control: the reminder tells them something is due; they decide what actually happened
- The dashboard pending count gives visibility into reminders awaiting confirmation without cluttering the transaction list
- Slightly more friction than auto-create, but the friction is intentional — each confirmation is a data quality checkpoint
- `NextDueDate` advancement logic must be implemented per frequency type (monthly, weekly, biweekly, annual) in the application layer; EF Core does not handle this automatically
- Deleting a template does not affect previously confirmed transactions (hard delete is safe on the template; confirmed transactions are independent records)
