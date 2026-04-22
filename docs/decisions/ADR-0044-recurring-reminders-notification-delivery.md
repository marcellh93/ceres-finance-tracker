# ADR 0044: Recurring Reminders — Notification Delivery

## Status: Accepted

## Context

Phase 1 reminders are pull-based — the user opens the app to see what is due. No proactive
notification exists. Phase 2 must decide whether to add push delivery or improve the
in-app experience.

Two options were considered:

**Push notifications (macOS or email)**
Proactive delivery without opening the app. Requires a background process checking due
dates and firing notifications. On macOS (Phase 2, local), this means a `launchd` daemon
or a .NET `IHostedService` running permanently. In Phase 3 (hosted), the background
process must co-exist with the web server — additional infrastructure complexity on top of
auth, multi-tenancy, and hosting concerns that are already Phase 3's primary scope.
Email push in Phase 3 requires an email service provider — already an open Phase 3
dependency.

**In-app improvements**
Visibility enhancements requiring only UI work — no background process, no infrastructure
complexity. Examples: dashboard banner, dedicated reminders view, nav badge count.

The primary Phase 2 goal for reminders is to validate that the reminder logic (due date
calculation, `DayOfPeriod` behaviour, advance/dismiss flow) works correctly in daily use.
Push delivery adds no value to that validation — it layers on top of logic that must first
be proven correct.

## Decision

**In-app only for Phase 2. Email push notifications deferred to Phase 3.**

### Phase 2 in-app improvements

- **Dashboard banner** — persistent indicator when reminders are due within the next 7
  days: "3 reminders due in the next 7 days"
- **Dedicated reminders view** — full list of upcoming reminders sorted by due date,
  showing name, amount, account, and days until due
- **Nav badge count** — numeric badge on the reminders nav item showing overdue +
  due-today count

No background process. No `IHostedService`. All reminder data is derived from existing
`RecurringTransaction` records on page load.

### Phase 3 — email push

When the Phase 3 email service is introduced (already a Phase 3 dependency for
registration, password reset, and audit notifications), reminder email delivery is added
on top:

- Daily digest email: reminders due in the next N days
- Individual reminder email: sent on the due date
- User-configurable: opt in/out per reminder or globally via settings

The background process required for email delivery is designed as part of Phase 3
infrastructure, not introduced prematurely in Phase 2.

## Consequences

**Positive:**
- No background process in Phase 2 — no `launchd` setup, no hosted service complexity
- Reminder logic validated in daily use before push delivery is layered on
- Email push lands naturally in Phase 3 alongside the email service that is already needed
- In-app improvements provide meaningful visibility without infrastructure cost

**Negative:**
- Reminders require the user to open the app — a due reminder can be missed if the app
  is not opened that day. Accepted as a Phase 2 limitation.
