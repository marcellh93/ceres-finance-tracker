# ADR 0051: Upcoming Payments View

## Status: Accepted

## Context

Users need a forward-looking view of payments they already know are coming — to answer
"do I have enough to cover what's due this month?" before they've already spent the money.

The original framing ("upcoming obligations") was too broad. The feature is specifically
a read-only view of payments the user has already configured as recurring transaction
templates, compared against current account balances. It is not a general obligation
tracker and does not require a new entity.

A secondary question arose around variable-interval bills (e.g. electricity every ~3
months, variable amount). These don't fit neatly into the existing `SnapToCalendarDay`
or `RelativeToLastConfirmation` reminder behaviours — the interval is too unpredictable
to estimate reliably.

## Decision

### Upcoming payments view

A read-only view pulling from existing `RecurringTransaction` templates due within the
next 30 days, shown alongside current account balances:

```
Upcoming this month:
  Rent          due Apr 30   €900     Checking
  Phone bill    due Apr 28   €45      Checking
  ─────────────────────────────────────────────
  Total due:    €945
  Checking:     €1,100
  Headroom:     €155
```

- Data source: `RecurringTransaction` templates only — no new entity
- Distinct from the reminders system: this is a planning view, not a confirmation flow
- Named "Upcoming Payments" — not "obligations"

### `ReminderBehaviour` extended with `ManualDate`

ADR-0045 introduced `SnapToCalendarDay` and `RelativeToLastConfirmation`. A third value
is added: `ManualDate`.

| Behaviour | `DayOfPeriod` | Interval field | Next due date set by |
|---|---|---|---|
| `SnapToCalendarDay` | Shown, required | Shown | Calculated from `DayOfPeriod` |
| `RelativeToLastConfirmation` | Hidden | Shown | Confirmation date + interval |
| `ManualDate` | Hidden | Hidden | User sets manually on each confirmation |

When `ManualDate` is selected, the confirmation prompt asks:

> "When do you expect the next one?"
> [date picker — no pre-fill]

The user sets the next due date based on what they actually know. No algorithm guesses.
This handles variable-interval bills (electricity, water, quarterly fees) where the
interval is too unpredictable to estimate reliably.

### One-off payments

A truly one-off payment is not recurring by definition. For one-off known payments, the
user creates a recurring template with `ManualDate` behaviour and simply does not advance
it after confirming — the template remains as a single future entry in the upcoming
payments view until confirmed, then is dismissed.

No separate "expected obligation" entity is introduced. The recurring transaction model
handles all cases.

## Consequences

**Positive:**
- No new entity — upcoming payments view is a query over existing `RecurringTransaction`
  data
- `ManualDate` behaviour handles variable-interval bills without pretending to know the
  interval
- One-off payments fit the existing model naturally with `ManualDate`
- The view answers a concrete, high-value question with zero schema additions beyond the
  `ReminderBehaviour` enum extension

**Negative:**
- Variable-interval bills require the user to set the next date manually on every
  confirmation — slightly more friction than auto-calculated dates
- The view only covers payments the user has configured as templates — ad-hoc or forgotten
  bills are not surfaced
