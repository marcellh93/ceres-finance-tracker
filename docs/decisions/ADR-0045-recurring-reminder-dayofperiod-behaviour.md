# ADR 0045: Recurring Reminder DayOfPeriod Behaviour

## Status: Accepted

## Context

`RecurringTransaction` has a `DayOfPeriod` field that is stored but currently ignored.
When a reminder is confirmed (advanced to its next due date), the behaviour of
`AdvanceDueDate` needed to be defined.

Two behaviours were identified:

**Snap to calendar day** — next due date always lands on `DayOfPeriod`, regardless of
when the user confirmed. If the user confirms late, the date snaps forward to the next
occurrence of that calendar day.

Example: rent reminder set for the 15th. Confirmed late on April 18th.
→ Next due date: May 15th (not May 18th).

**Relative to last confirmation** — next due date = confirmation date + period length.
The anchor is when the user last confirmed, not a fixed calendar position.

Example: gym habit reminder, every 3 days. Confirmed April 14th.
→ Next due date: April 17th.

These two behaviours suit different use cases:

| Behaviour | Best for | Problem if misapplied |
|---|---|---|
| Snap to calendar day | Fixed bills, subscriptions, loan payments | Can produce a past date if period is short and confirmed very late |
| Relative to last confirmation | Flexible habits, routines, check-ins | Drifts over time — a monthly habit becomes every 5–6 weeks |

Both use cases are legitimate. A single universal behaviour would force one group of users
into an incorrect model.

## Decision

**Both behaviours coexist.** `ReminderBehaviour` enum added to `RecurringTransaction`:

```csharp
public enum ReminderBehaviour
{
    SnapToCalendarDay,          // default
    RelativeToLastConfirmation
}
```

### `SnapToCalendarDay` (default)

`DayOfPeriod` is the anchor. `AdvanceDueDate` calculates the next occurrence of
`DayOfPeriod` after today, always snapping forward — never backward.

```
Next due date = next future date where day-of-month == DayOfPeriod
```

If `DayOfPeriod` is 31 and the next month has fewer days, snap to the last day of that
month.

### `RelativeToLastConfirmation`

`DayOfPeriod` is ignored. `AdvanceDueDate` adds the period length to the confirmation
date.

```
Next due date = confirmation date + period length
```

### Default

`SnapToCalendarDay` is the default at reminder creation. The form presents:

> **Scheduling**
> ○ Fixed date (e.g. always the 15th) ← default
> ○ Flexible (relative to when I last confirmed)

The flexible option is shown without technical language. Most users will never change the
default.

### Schema addition

```
RecurringTransaction
  + ReminderBehaviour  varchar NOT NULL DEFAULT 'SnapToCalendarDay'
```

## Consequences

**Positive:**
- Fixed bills behave predictably — always land on the configured day
- Flexible habits don't drift — they advance from the actual confirmation date
- Default is correct for the majority use case — users who need relative behaviour opt in
- `DayOfPeriod` now has a clearly defined role: anchor for snap behaviour, ignored for
  relative

**Negative:**
- `AdvanceDueDate` has two code paths to maintain and test — both must be covered by unit
  tests
- Edge cases for snap behaviour (late months, leap years, confirming after the next
  occurrence has already passed) require careful handling
