# ADR 0009: Transaction Dates Stored as Local Date Without Timezone

## Status: Accepted

## Context
Transaction and transfer records need a date. The options are:

1. **`datetime` with UTC storage:** Store the full timestamp in UTC, convert to the user's
   local timezone for display and report period boundaries. Correct across timezones but
   requires timezone-aware rendering throughout and a user timezone setting.

2. **`date` (local date, no time, no timezone):** Store only the calendar date the user
   says the transaction occurred. No timezone conversion. Report boundaries (start of month,
   end of year) are simply calendar comparisons on the stored date value.

In Phase 1 the app runs locally for a single user in a known timezone. The user records
transactions as they happen or enter them manually. The meaningful unit is the calendar
date — whether a grocery run happened at 2pm or 11pm is irrelevant; what matters is
which month it belongs to for reporting purposes.

Storing UTC timestamps adds complexity (timezone offset stored where? Settings? Browser?
Server locale?) with no practical benefit for a single-user local app.

## Decision
All date fields on Transaction and Transfer use SQL Server's `date` type — a calendar date
with no time component and no timezone. The date is whatever the user enters or selects.
Report period boundaries are calendar comparisons (WHERE Date >= '2026-01-01' AND Date < '2026-02-01').

This decision is explicitly scoped to Phase 1 and 2. Before Phase 3, the timezone strategy
must be revisited because multi-user hosting introduces users in different timezones —
"end of January" means different calendar dates for a user in Madrid vs. one in Bogotá.
See Open Questions in planning.md.

## Consequences

**Positive:**
- Simple to implement — no timezone conversion logic anywhere in Phase 1/2
- Report queries are straightforward calendar date comparisons
- User input is intuitive — the user enters the date they experienced, not a UTC timestamp
- No timezone setting required in Phase 1/2

**Negative:**
- Phase 3 must revisit this decision. If the app grows to multi-timezone users, report
  boundaries will be inconsistent without a timezone-aware approach. Migrating from `date`
  to `datetime` + timezone requires a schema change and a decision on what timezone to
  assign to all existing records.
- If a user crosses a timezone boundary (travelling) and enters a transaction, the "local
  date" is ambiguous — this is an acceptable limitation for Phase 1/2.
- No time component means you cannot order transactions within a single day by entry time
  — within a day, order is undefined.
