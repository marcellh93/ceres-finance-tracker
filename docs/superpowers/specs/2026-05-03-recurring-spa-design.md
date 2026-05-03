# Recurring Transactions SPA — Design Spec

> **Status:** Spec locked 2026-05-03. Committed alongside implementation.

---

## 1. Context

**Why this is happening.** Phase 3 SPA migration Batch 2 #4. The Razor `RecurringTransactionsController` and its 8 views (`Index`, `Create`, `Edit`, `Confirm`, `Dismiss`, `Deactivate`, `Upcoming`, `_ReminderTable`) get retired in favour of a React SPA. The API has shipped fully since `1bd4fff` (full CRUD + Confirm + Dismiss + Upcoming + Archive); this is mostly a frontend exercise plus a Razor cutover plus two well-bounded server changes — Weekly/Biweekly Snap fix and a new Reactivate endpoint.

**What's new vs Categories/Accounts patterns.**

- **Two stateful actions beyond CRUD** — Confirm (creates a Transaction, advances NextDueDate) and Dismiss (advances NextDueDate, no transaction). These have no precedent in the prior Batch 2 pages.
- **ReminderBehaviour discriminator** — three values (`SnapToCalendarDay`, `RelativeToLastConfirmation`, `ManualDate`) drive conditional form fields and the Confirm/Dismiss flow shapes.
- **Frequency-driven day picker** — DayOfPeriod means day-of-week (1–7) for Weekly/Biweekly+Snap and day-of-month (1–31) for Monthly+Snap. The current server ignores it for Weekly/Biweekly — that's a real bug being fixed in this scope.
- **Topbar bell wiring** — the existing stub Popover at `TopBar.tsx:98–113` becomes a live due/overdue surface backed by the same `/api/recurring-transactions/upcoming?days=0` endpoint.
- **Reactivate, finally** — your saved feedback says "Archive always ships with Reactivate." Categories/Accounts shipped without it; Recurring breaks that streak. Categories/Accounts retro-fit is logged separately.
- **Estimated amount becomes properly nullable** — null = "amount varies", 0 = a real €0. Aligns with Dashboard's existing `imminentBills` filter.

## 2. Goals & non-goals

### Goals

- Replace the placeholder `/app/recurring` route with a real Recurring SPA: list, Create, Edit, Confirm dialog, Dismiss dialog, Archive dialog, Reactivate.
- Mirror the Razor surface area so no capability regresses on cutover.
- Apply the locked SPA-page template established by Settings, Categories, and Accounts. No deviation; deviations explicitly flagged.
- Slim the Razor `RecurringTransactionsController` to redirects matching the Categories/Accounts cutover pattern.
- Wire the topbar bell to show due+overdue count and a popover list, replacing the "no notifications" stub.
- **Server change 1:** Fix `RecurringTransactionService.SnapToCalendarDay` to honour DayOfPeriod for Weekly+Biweekly (snap to weekday).
- **Server change 2:** Add `PATCH /api/recurring-transactions/:id/reactivate` endpoint + `TryReactivateAsync` service method.
- **Server change 3:** Make `EstimatedAmount` properly nullable on the API request DTOs (entity is already `decimal?`); remove the `Range` lower bound that today silently coerces null to 0.
- **Server change 4:** Tighten `RecurringTransactionPolicies` (new file, mirror of `AccountPolicies`) to validate DayOfPeriod range against Frequency: 1–7 for Weekly/Biweekly, 1–31 for Monthly, must be null for Annual or non-Snap.
- Fix doc/code drift in `models.md`: Dismiss DOES advance NextDueDate. EstimatedAmount IS nullable.

### Non-goals

- **No reactivate retro-fit for Categories or Accounts.** Logged separately as a follow-up commit.
- **No Transaction → Reminder FK.** Logged to `planning-future.md`.
- **No background scheduler / push notifications.** ADR-0044 puts that behind email service; out of scope.
- **No new Upcoming route.** The unified list with status badges + topbar bell + dashboard RemindersCard already cover the surface.
- **No changes to Movements.** A confirmed-from-reminder Transaction looks identical to a manually-entered one; provenance link deferred.
- **No new ADR.** Existing ADR-0044/0045/0051 cover the behaviour space.
- **No DELETE endpoint** for reminders — Archive (soft) is sufficient and matches the rest of the system.

---

## 3. File structure

### Create — client

```
ProjectCeres.Client/src/app/features/recurring/
  recurring-api.ts                  ← URL builders + DTOs (logic-free)
  RecurringLayout.tsx               ← list page glue (Outlet host)
  RecurringLayout.test.tsx
  RecurringTable.tsx                ← pure UI table
  RecurringTable.test.tsx
  RecurringRowMenu.tsx              ← ⋯ menu + Confirm/Dismiss/Archive AlertDialogs + Reactivate item
  RecurringRowMenu.test.tsx
  RecurringForm.tsx                 ← shared Create/Edit form, conditional fields
  RecurringForm.test.tsx
  RecurringCreate.tsx               ← page glue for /new
  RecurringCreate.test.tsx
  RecurringEdit.tsx                 ← page glue for /:id/edit
  RecurringEdit.test.tsx
  RecurringConfirmDialog.tsx        ← AlertDialog body for Confirm flow
  RecurringConfirmDialog.test.tsx
  RecurringDismissDialog.tsx        ← AlertDialog body for Dismiss flow
  RecurringDismissDialog.test.tsx
  reminder-status.ts                ← pure helpers: classifyStatus(reminder) → 'overdue' | 'dueToday' | 'upcoming' | 'archived'; badge labels
  reminder-status.test.ts
```

Plus a small shared topbar piece:

```
ProjectCeres.Client/src/app/layout/
  ReminderCountProvider.tsx         ← Context for bell badge count + refresh()
  ReminderCountProvider.test.tsx
  TopBar.tsx                        ← MODIFIED: NotificationsButton uses ctx, renders list, links to /recurring
  TopBar.test.tsx                   ← MODIFIED tests for the new behaviour
```

### Modify — client

- `ProjectCeres.Client/src/app/pages/Recurring.tsx` — replace placeholder with one-line re-export `export { RecurringLayout as Recurring } from '../features/recurring/RecurringLayout';`.
- `ProjectCeres.Client/src/app/App.tsx` — convert flat `<Route path="recurring" element={<Recurring />} />` into the nested form with `/new` and `/:id/edit` children. Wrap the app shell in `<ReminderCountProvider>`.
- `ProjectCeres.Client/src/app/layout/TopBar.tsx` — `NotificationsButton` now reads from the provider, renders a list of due+overdue reminders, links to `/recurring`.
- `ProjectCeres.Client/src/app/App.test.tsx` — the placeholder test (`expects h1 'Recurring Transactions'`) flips to expect the live page.
- `ProjectCeres.Client/src/app/layout/Sidebar.test.tsx` — no change needed (sidebar entry already exists).
- `ProjectCeres.Client/src/components/ui/navbar.tsx:90` — drop the legacy "/RecurringTransactions/Upcoming" link (the Razor route is going away). Topbar bell replaces it. *Note: verify this file is still in use; if it's a Razor-era component already deleted in earlier batches, skip.*

### Modify — server

- `ProjectCeres/Controllers/RecurringTransactionsController.cs` — slim to redirects (see §8c).
- `ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs` — add `Reactivate` action.
- `ProjectCeres/Services/IRecurringTransactionService.cs` — drop throwing CRUD declarations (`CreateAsync(vm)`, `UpdateAsync(vm)`, `DeactivateAsync`, throwing `ConfirmAsync`, throwing `DismissAsync`); add `Result<...> TryReactivateAsync(Guid id)`.
- `ProjectCeres/Services/RecurringTransactionService.cs` — drop the throwing method bodies; add `TryReactivateAsync`; rewrite `SnapToCalendarDay` to handle Weekly+Biweekly+DayOfPeriod (see §8a).
- `ProjectCeres/Services/RecurringTransactionPolicies.cs` — **NEW FILE.** Mirror of `AccountPolicies`. Validates `DayOfPeriod` against `Frequency` and `ReminderBehaviour`. See §8b.
- `ProjectCeres/ViewModels/RecurringTransactionApiDtos.cs` — make `EstimatedAmount` properly nullable on `CreateRecurringTransactionRequest` and `UpdateRecurringTransactionRequest` (drop `Range(0, ...)` on a non-nullable decimal in favour of `Range(0, ...)` on `decimal?`). Other DTOs unchanged.
- `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs` — drop tests against deleted throwing methods. Add tests for the Weekly+Biweekly+Snap fix and Reactivate.
- `ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs` — add Reactivate tests; add tests asserting EstimatedAmount round-trips null.
- `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs` — drop `RazorRecurringTransactionService_*_stamps_UserId` tests if they exist (Categories/Accounts precedent).
- `docs/models.md` — fix two drift items: Dismiss advances NextDueDate; EstimatedAmount NULL semantics. Add the Weekly+Biweekly Snap behaviour clarification.

### Delete — server

- `ProjectCeres/Views/RecurringTransactions/Index.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Create.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Edit.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Confirm.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Dismiss.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Deactivate.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml`
- `ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml`
- `ProjectCeres/ViewModels/RecurringTransactionCreateViewModel.cs`
- `ProjectCeres/ViewModels/RecurringTransactionEditViewModel.cs`

---

## 4. Routing & navigation

### Route structure (`App.tsx`)

```tsx
<Route path="recurring" element={<Recurring />}>
  <Route path="new" element={<RecurringCreate />} />
  <Route path=":id/edit" element={<RecurringEdit />} />
</Route>
```

`<Recurring />` is the one-line re-export of `RecurringLayout`. Confirm and Dismiss are AlertDialogs spawned from the row menu — no route. Archive and Reactivate likewise have no route.

### `RecurringLayout` Outlet rendering rules

Same `childActive` pattern Categories/Accounts use:

- When `useMatch('/recurring/new')` or `useMatch('/recurring/:id/edit')` is active → render only `<Outlet />` inside an `mx-auto max-w-4xl space-y-6` container; the layout's list chrome is suppressed.
- Otherwise → render the full list page.
- The Outlet provides `{ refetch: () => void, refreshBell: () => void }` so child pages refresh both the parent list cache AND the topbar bell after a successful mutation.

### Page width

- List page: **`max-w-4xl` (896px)**, centered. Wider than Categories/Accounts list (768px) because Recurring has 6 data columns.
- Create / Edit forms: also `max-w-4xl`. Inherits the layout container.

### Navigation entry points

| Destination | Triggered from |
|---|---|
| `/recurring` | Sidebar nav item "Recurring transactions"; "← Back to Recurring" Cancel on Create/Edit; topbar bell popover footer; dashboard RemindersCard "View all →"; post-Create / post-Edit redirect |
| `/recurring/new` | "+ New reminder" button in list-page filter row |
| `/recurring/:id/edit` | `⋯ → Edit` in row menu (active rows only) |

### URL state (list page, search params)

- `?q=<search>` — debounced 200ms, client-side filter on Name. Drives the search input value.
- `?includeInactive=true` — toggled by the Include archived switch; absent when off.
- No `?frequency=` or `?status=` filter params.

---

## 5. List page (`/app/recurring`)

### Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Recurring transactions                                                        │  ← h1, max-w-4xl
│ Templates that confirm into real transactions on a schedule. Confirm a       │
│ reminder when it actually happens; dismiss it when you want to skip.         │
├──────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────────────────┐ │
│ │  [Filter reminders…           ]                       [+ New reminder]  │ │
│ │  ◯ Include archived                                                       │ │
│ │                                                                            │ │
│ │  Name                Account     Category   Frequency  Est.    Next due   │ │
│ │  ───────────────────────────────────────────────────────────────────     │ │
│ │  Rent  [Overdue]     Checking    Housing    Monthly    €900    28/04/2026 │ │
│ │  Salary  [Due today] Checking    Salary     Monthly    €2,500  03/05/2026 │ │
│ │  Internet  [Due today] Checking  Bills      Monthly    €35     03/05/2026 │ │
│ │  Netflix             Checking    Subscript… Monthly    €12.99  12/05/2026 │ │
│ │  Gym  [Manual]       Checking    Wellness   Monthly    €40     05/06/2026 │ │
│ │  Variable bill       Checking    Utilities  Monthly    —       15/05/2026 │ │
│ └──────────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Header

- `<h1>` "Recurring transactions" (text-2xl font-semibold). `tabIndex={-1}`, focused on mount via headingRef.
- One-line muted description below the h1: "Templates that confirm into real transactions on a schedule. Confirm a reminder when it actually happens; dismiss it when you want to skip."

### Filter row (inside the list Card, above the table)

- Search Input — placeholder "Filter reminders…", debounced 200ms via `useDebounced`, client-side filter via `reminder.name.toLowerCase().includes(query.toLowerCase())`.
- "+ New reminder" button — primary variant, `<Link to="new">` rendered via `<Button render={<Link>...</Link>}>`. Right-aligned in the flex row.
- "Include archived" Switch — separate row beneath the search/button row, drives `?includeInactive=true` on the URL and the API URL.

### Table (`RecurringTable`)

Six columns + `⋯` slot:

| Column | Width | Alignment | Notes |
|---|---|---|---|
| Name | flex-1 | left | Inline status badges next to name (gap-2): Overdue / Due today / Manual / Archived |
| Account | w-32 | left | `accountName` from joined DTO |
| Category | w-32 | left | `categoryName` from joined DTO; truncate with tooltip on overflow |
| Frequency | w-24 | left | "Weekly", "Biweekly", "Monthly", "Annual" — humanised |
| Est. amount | w-24 | right, `tabular-nums` | `{currencySymbol}{amount}` using user's NumberFormat. **Renders `—` when EstimatedAmount is null** ("varies") |
| Next due | w-32 | left | User's DateFormat from Settings |
| `⋯` | w-12 | center | Row menu trigger |

**Sort:** by `NextDueDate` ascending. Active rows first, archived last (when "Include archived" is on). Within each group, `NextDueDate` ascending. Single sort. No user-toggleable sort.

**Archived row treatment:** `opacity-60` on the `<tr>` + inline `<Badge variant="secondary">Archived</Badge>` next to the name.

**Status badge logic** (in `reminder-status.ts`):

```typescript
type ReminderStatus = 'overdue' | 'dueToday' | 'upcoming' | 'archived';

function classifyStatus(reminder, today): ReminderStatus[] {
  const out: ReminderStatus[] = [];
  if (!reminder.isActive) {
    out.push('archived');
    return out;
  }
  if (reminder.nextDueDate < today) out.push('overdue');
  else if (reminder.nextDueDate === today) out.push('dueToday');
  if (reminder.reminderBehaviour === 'ManualDate') out.push('manual');
  return out;
}
```

A row can carry both `overdue` and `manual` (e.g. an overdue ManualDate reminder). Both render, in that order.

### Empty states

| Branch | Detection | Render |
|---|---|---|
| First-run / zero reminders | Server returns empty list AND a separate `?includeInactive=true` count call also returns 0 | Inside the list Card body, large centered area: heading "No recurring reminders yet", body "Set up reminders for bills, subscriptions, salary — anything that recurs.", primary `+ New reminder` button |
| Zero active but archived ones exist | Default view (no `includeInactive`) returns empty, but `reminders.some(r => !r.isActive)` is true after fetching with the toggle | Italic muted text: "No active reminders." Include archived toggle is already on screen |
| Search returned nothing | `sorted.length === 0 && query.length > 0` | Italic muted text: "No reminders match '{query}'." + ghost `Clear search` link button |

### Loading state

Five `<Skeleton>` rows (h-9, full width) inside the table area.

### Error state

`<CardError section="Recurring transactions" onRetry={list.refetch} />`.

---

## 6. Form (Create + Edit shared shape)

### Container

Both Create and Edit render inside a single Card. Title: "New reminder" / "Edit reminder". Card content: `<RecurringForm>` + form-level button row. Card width inherits the layout's `max-w-4xl`.

### Field order (top-down)

```
┌─ New reminder ─────────────────────────────────────────────────────────┐
│  Name *                  [_______________________________________]      │
│  Account *               [Checking                          ▾]          │
│  Category *              [Bills                             ▾]          │
│  Estimated amount        [_______________________________________]      │
│    Leave blank if the amount varies each time. Variable-amount         │
│    reminders are skipped by spendable-balance forecasts.               │
│                                                                          │
│  Frequency *             [Monthly                           ▾]          │
│  Reminder behaviour *    [Snap to calendar day              ▾]          │
│                                                                          │
│  ─── (Snap + Weekly/Biweekly only) ───                                 │
│  Day of week *           [Monday                            ▾]          │
│                                                                          │
│  ─── (Snap + Monthly only) ───                                          │
│  Day of month *          [____]                                         │
│    1–31. If a month has fewer days, snaps to the last day.             │
│                                                                          │
│  Next due date *         [03/05/2026                          📅]       │
│                                                                          │
│  [Save]   [Cancel]                                                      │
└────────────────────────────────────────────────────────────────────────┘
```

### Field details

| Field | Required | Edit behaviour | Validation |
|---|---|---|---|
| Name | yes | editable | trim, 1–100 chars |
| Account (`AccountId`) | yes | editable | one of user's active accounts |
| Category (`CategoryId`) | yes | editable | one of user's active or shared categories |
| Estimated amount | no (null = "varies") | editable | null or 0 ≤ x ≤ 999,999,999,999.99 |
| Frequency | yes | editable | Weekly / Biweekly / Monthly / Annual |
| ReminderBehaviour | yes | editable | SnapToCalendarDay / RelativeToLastConfirmation / ManualDate |
| Day of week / month | conditional | editable | Snap+Weekly|Biweekly: 1–7. Snap+Monthly: 1–31. Otherwise: must be null |
| Next due date | yes | editable | DateOnly; defaults to today on Create. Must be ≥ Account's opening balance date (server enforces). |

### Conditional rendering rules

**Behaviour=SnapToCalendarDay:**
- Frequency=Weekly or Biweekly → render "Day of week" Popover+Command picker (Mon/Tue/Wed/Thu/Fri/Sat/Sun, value 1–7).
- Frequency=Monthly → render "Day of month" number input (1–31).
- Frequency=Annual → no day picker (NextDueDate carries the day-of-year).

**Behaviour=RelativeToLastConfirmation:**
- No day picker, regardless of Frequency. NextDueDate alone defines the next event; subsequent advances are confirmDate + interval.

**Behaviour=ManualDate:**
- No day picker, regardless of Frequency. Frequency drives the dashboard's monthly-bills calculation (so it still must be set), but the schedule advance comes from the user on each Confirm.

**Cached value pattern (Accounts precedent):** when conditions hide the day field, the underlying form-state value is preserved. Switching back to a configuration that needs it restores the prior selection (within range — i.e. switching Monthly day=15 → Weekly clears since 15 isn't a valid weekday; switching back to Monthly restores 15).

### Submit-time normalisation (Layer 1)

```typescript
const body = {
  ...values,
  estimatedAmount: values.estimatedAmount === '' ? null : Number(values.estimatedAmount),
  dayOfPeriod: shouldSendDayOfPeriod(values.frequency, values.reminderBehaviour, values.dayOfPeriod)
    ? values.dayOfPeriod
    : null,
};

function shouldSendDayOfPeriod(frequency, behaviour, value) {
  if (behaviour !== 'SnapToCalendarDay') return false;
  if (frequency === 'Annual') return false;
  return value != null;
}
```

### Buttons

- **Save** — primary variant. Disabled until form is dirty (state differs from `snapshot`). Shows "Saving…" while the request is in-flight.
- **Cancel** — `variant="outline"`. Calls `navigate('/recurring')`.
- **No Reset button.** (Same convention as Categories/Accounts.)

### Error handling on submit

- 2xx → `toast.success("Created.")` (Create) or `toast.success("Saved.")` (Edit) → `ctx.refetch()` → `ctx.refreshBell()` → `navigate('/recurring')`.
- 422 → `toast.error("Couldn't save. Try again.")` → form retains values.
- Network error → same generic toast.

### Skeletons during initial load (Edit only)

Edit fetches reminder detail (`GET /api/recurring-transactions/:id`), accounts list (`GET /api/accounts`), categories list (`GET /api/categories`). While any is loading: Card with title + 6 Skeleton rows.

Create only fetches accounts + categories. Same skeleton shell.

### Error branches

- `GET /api/recurring-transactions/:id` returns 404 → "That reminder doesn't exist." banner + "← Back to Recurring" button.
- `GET /api/accounts` or `GET /api/categories` errors → generic "Couldn't load form data." banner with Retry.

### Pickers

**Account, Category, Frequency, ReminderBehaviour, Day of week** — all use the locked Popover+Command idiom. Day of month is a number input (`<input type="number" min={1} max={31} />`).

---

## 7. Row menu + dialog flows

### Row menu (`RecurringRowMenu`)

| Row state | Menu items |
|---|---|
| Active | Confirm… · Edit · Dismiss… · Archive… |
| Archived | Reactivate |

### Confirm flow (`RecurringConfirmDialog`)

Active row only. Click `⋯ → Confirm…` opens an AlertDialog.

```
┌─ Confirm 'Rent'? ──────────────────────────────────────────────┐
│ Record this reminder as a transaction.                         │
│                                                                  │
│ Date *           [03/05/2026                    📅]             │
│ Amount *         [_____________________________900.00]          │
│ Description      [Rent                                  ]       │  ← placeholder = reminder.Name
│                                                                  │
│ (ManualDate only)                                                │
│ Next due date *  [01/06/2026                    📅]             │
│                                                                  │
│                          [Cancel]  [Confirm — record]           │
└────────────────────────────────────────────────────────────────┘
```

**Defaults populated when dialog opens:**
- Date = `reminder.nextDueDate`
- Amount = `reminder.estimatedAmount ?? ''` (empty when null; user must enter)
- Description = `''` (placeholder shows `reminder.name`; submitting empty sends `null`, server falls back to reminder.name)
- Next due date (ManualDate only) = empty (user must pick)

**Submit:** `POST /api/recurring-transactions/:id/confirm` body `{ date, amount, description: description || null, nextDueDate: reminderBehaviour === 'ManualDate' ? nextDueDate : null }`.

**Response handling:**
- 201 with `{ transactionId }` → `toast.success(\`Recorded ${formattedAmount} on ${formattedDate}.\`)`. Toast includes a "View transaction →" action that navigates to `/movements?selected={transactionId}` (or whatever the current Movements deep-link pattern is — verify against Movements code at plan-writing time). `ctx.refetch()` + `ctx.refreshBell()`. Dialog closes.
- 422 with `DATE_BEFORE_OPENING_BALANCE` → inline error in dialog ("That date is before this account's opening balance."), form retains values, no toast.
- 422 with `NEXT_DUE_DATE_REQUIRED` → inline error on the Next due date field. Should never fire (the form gates the field on ManualDate) but catch it defensively.
- Other 422 → generic `toast.error("Couldn't record. Try again.")`, form retains values.

### Dismiss flow (`RecurringDismissDialog`)

Active row only. Click `⋯ → Dismiss…` opens an AlertDialog.

**Snap / Relative reminders:**

```
┌─ Dismiss 'Rent'? ──────────────────────────────────────────────┐
│ No transaction will be recorded. The next due date advances    │
│ to the following period.                                       │
│                                                                  │
│                                  [Cancel]  [Dismiss]           │
└────────────────────────────────────────────────────────────────┘
```

**ManualDate reminders:**

```
┌─ Dismiss 'Gym'? ───────────────────────────────────────────────┐
│ No transaction will be recorded. Pick the next due date        │
│ manually.                                                      │
│                                                                  │
│ Next due date *  [01/06/2026                    📅]             │
│                                                                  │
│                                  [Cancel]  [Dismiss]           │
└────────────────────────────────────────────────────────────────┘
```

**Submit:** `POST /api/recurring-transactions/:id/dismiss` body `{ nextDueDate: reminderBehaviour === 'ManualDate' ? nextDueDate : null }`.

> **Note:** the existing API contract has `Dismiss` as 204 with no body. The SPA will send a body with `nextDueDate` for ManualDate reminders. **If the controller doesn't currently accept a body on Dismiss, this is a small server change** — the controller binds `[FromBody] DismissRecurringTransactionRequest? body` and forwards `body?.NextDueDate` to `TryDismissAsync`. Currently `TryDismissAsync` accepts no params; signature becomes `Result<...> TryDismissAsync(Guid id, DateOnly? nextDueDate)`. Plan-writing step verifies the exact current shape.

**Response handling:**
- 204 → `toast.success(\`Dismissed. Next due date advanced to ${formattedNextDueDate}.\`)`. `ctx.refetch()` + `ctx.refreshBell()`. Dialog closes.
- 422 → generic `toast.error("Couldn't dismiss. Try again.")`, dialog stays open.

### Archive flow

Active row only. Click `⋯ → Archive…` opens an AlertDialog.

```
┌─ Archive 'Rent'? ──────────────────────────────────────────────┐
│ This reminder will stop appearing in the active list and       │
│ will not advance any further. Existing transactions stay       │
│ attached to your account history. You can reactivate it        │
│ later from the archived list.                                  │
│                                                                  │
│                                  [Cancel]  [Archive]           │
└────────────────────────────────────────────────────────────────┘
```

**Submit:** `PATCH /api/recurring-transactions/:id/archive` no body.

**Response handling:**
- 204 → `toast.success("Archived.")`. `ctx.refetch()` + `ctx.refreshBell()`. Dialog closes.
- Other → `toast.error("Couldn't archive. Try again.")`.

### Reactivate flow

Archived row only. Click `⋯ → Reactivate` — **no AlertDialog** (reverse action is benign).

**Submit:** `PATCH /api/recurring-transactions/:id/reactivate` no body. *(New endpoint — see §8c.)*

**Response handling:**
- 204 → `toast.success("Reactivated.")`. `ctx.refetch()` + `ctx.refreshBell()`. Row re-appears in the active section.
- Other → `toast.error("Couldn't reactivate. Try again.")`.

---

## 8. Server-side changes

Four areas of server work, in dependency order.

### 8a. Fix Weekly+Biweekly Snap-to-calendar-day in `RecurringTransactionService.SnapToCalendarDay`

**Current** (`RecurringTransactionService.cs:154–178`):

```csharp
private static DateOnly SnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
{
    if (reminder.DayOfPeriod is null || reminder.Frequency != Frequency.Monthly)
    {
        return AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency);
    }
    // …existing monthly-day clamp logic…
}
```

**New:**

```csharp
private static DateOnly SnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
{
    if (reminder.DayOfPeriod is null)
        return AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency);

    return reminder.Frequency switch
    {
        Frequency.Monthly  => SnapMonthly(reminder, confirmDate),
        Frequency.Weekly   => SnapWeekly(reminder, confirmDate, doubleStep: false),
        Frequency.Biweekly => SnapWeekly(reminder, confirmDate, doubleStep: true),
        // Annual: DayOfPeriod is meaningful only as day-of-year through NextDueDate.
        _                  => AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency),
    };
}

private static DateOnly SnapWeekly(RecurringTransaction reminder, DateOnly? confirmDate, bool doubleStep)
{
    // DayOfPeriod: 1=Monday … 7=Sunday (ISO 8601). DayOfWeek: Sunday=0 … Saturday=6.
    var targetDow = (DayOfWeek)(reminder.DayOfPeriod!.Value % 7);
    var from = confirmDate ?? reminder.NextDueDate;
    var daysAhead = ((int)targetDow - (int)from.DayOfWeek + 7) % 7;
    if (daysAhead == 0) daysAhead = 7;       // never return same-day
    if (doubleStep && daysAhead < 8) daysAhead += 7;
    return from.AddDays(daysAhead);
}
```

**Tests added** in `RecurringTransactionServiceTests`:
- `SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromEarlierInWeek` — confirm on Wed, target Mon, expect next Mon (5 days ahead).
- `SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromTargetDay` — confirm on Mon, target Mon, expect next Mon (7 days ahead).
- `SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromLaterInWeek` — confirm on Fri, target Wed, expect next Wed (5 days ahead).
- `SnapToCalendarDay_Biweekly_AdvancesAtLeast8Days` — confirm on Wed, target Wed, expect 14 days ahead (not 0, not 7).
- `SnapToCalendarDay_Biweekly_TargetMidweek` — confirm on Mon, target Wed, expect 16 days (Wed of week+2, since 2 days < 8 so add 7).

### 8b. New `RecurringTransactionPolicies` static class

Mirrors `AccountPolicies`. Single method `ValidateSchedule`:

```csharp
public static class RecurringTransactionPolicies
{
    public const string InvalidDayOfPeriodCode = "INVALID_DAY_OF_PERIOD";

    public static Result ValidateSchedule(Frequency frequency, ReminderBehaviour behaviour, int? dayOfPeriod)
    {
        // DayOfPeriod is meaningful only with SnapToCalendarDay AND non-Annual frequency.
        var snapNonAnnual = behaviour == ReminderBehaviour.SnapToCalendarDay
                         && frequency != Frequency.Annual;

        if (!snapNonAnnual && dayOfPeriod is not null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period applies only to Snap-to-calendar-day reminders that are not Annual.");

        if (snapNonAnnual && dayOfPeriod is null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period is required for Snap-to-calendar-day reminders.");

        if (snapNonAnnual)
        {
            var max = frequency == Frequency.Monthly ? 31 : 7;
            if (dayOfPeriod < 1 || dayOfPeriod > max)
                return Result.Fail(InvalidDayOfPeriodCode,
                    $"Day of period must be 1–{max} for {frequency} reminders.");
        }

        return Result.Ok();
    }
}
```

Called from `TryCreateAsync` and `TryUpdateAsync` before persistence. The SPA's submit-time normalisation (§6 Layer 1) keeps the wire payload self-consistent; this policy is Layer 2 and protects any future client.

**Tests added** in a new `RecurringTransactionPoliciesTests` (xUnit, no DB):
- `ValidateSchedule_SnapMonthly_WithDay15_Succeeds`
- `ValidateSchedule_SnapMonthly_WithDay32_Fails`
- `ValidateSchedule_SnapWeekly_WithDay8_Fails`
- `ValidateSchedule_SnapWeekly_WithDay4_Succeeds`
- `ValidateSchedule_Manual_WithDay15_Fails`
- `ValidateSchedule_SnapAnnual_WithDay15_Fails`
- `ValidateSchedule_SnapMonthly_WithNullDay_Fails`
- `ValidateSchedule_Manual_WithNullDay_Succeeds`

### 8c. Reactivate endpoint + service method

**Service:**

```csharp
public async Task<Result> TryReactivateAsync(Guid id)
{
    var user = currentUserAccessor.Get().UserId;
    var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
    if (reminder is null) return Result.Fail("NOT_FOUND");
    if (reminder.IsActive) return Result.Ok();   // idempotent
    reminder.IsActive = true;
    await db.SaveChangesAsync();
    return Result.Ok();
}
```

**Controller:**

```csharp
[HttpPatch("{id:guid}/reactivate")]
public async Task<IActionResult> Reactivate(Guid id)
{
    var result = await service.TryReactivateAsync(id);
    return result.IsSuccess ? NoContent() : ToErrorResponse(result);
}
```

**Tests added** in `RecurringTransactionsCrudApiTests`:
- `Reactivate_returns_204_for_archived_reminder`
- `Reactivate_returns_204_for_already_active_reminder` (idempotent)
- `Reactivate_returns_404_for_unknown_id`
- `Reactivate_returns_404_for_intruder_row`

### 8d. EstimatedAmount nullability cleanup

Update `CreateRecurringTransactionRequest` and `UpdateRecurringTransactionRequest`:

```csharp
public record CreateRecurringTransactionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 999_999_999_999.99)] decimal? EstimatedAmount,   // <- was non-nullable
    [Required] Guid? AccountId,
    [Required] Guid? CategoryId,
    [Required] string Frequency,
    [Range(1, 31)] int? DayOfPeriod,
    [Required] DateOnly NextDueDate,
    string ReminderBehaviour
);
```

Service `TryCreateAsync` / `TryUpdateAsync` already pass through `request.EstimatedAmount` to the entity which is `decimal?`. No service-side change needed.

**Tests added** in `RecurringTransactionsCrudApiTests`:
- `Post_with_null_estimated_amount_persists_null` — POST `{ estimatedAmount: null, ... }`, GET back, assert `estimatedAmount` is null.
- `Post_with_zero_estimated_amount_persists_zero` — POST `{ estimatedAmount: 0, ... }`, assert `estimatedAmount` is 0 (not coerced).
- `Patch_can_set_estimated_amount_to_null` — patch from non-null → null.
- `Patch_can_set_estimated_amount_to_value` — patch from null → 12.99.

### 8e. Razor cutover

**`RecurringTransactionsController` slims to redirects.**

```csharp
public class RecurringTransactionsController : Controller
{
    public IActionResult Index()                => Redirect("/app/recurring");
    public IActionResult Create()               => Redirect("/app/recurring/new");
    public IActionResult Edit(Guid id)          => Redirect($"/app/recurring/{id}/edit");
    public IActionResult Confirm(Guid id)       => Redirect("/app/recurring");
    public IActionResult Dismiss(Guid id)       => Redirect("/app/recurring");
    public IActionResult Deactivate(Guid id)    => Redirect("/app/recurring");
    public IActionResult Upcoming()             => Redirect("/app/recurring");
}
```

302 (default), not 301 — same cache reasoning as Categories/Accounts.

**`IRecurringTransactionService` and `RecurringTransactionService` — drop the throwing CRUD methods.**

Keep:
- `GetAllAsync(bool includeInactive)`
- `GetByIdAsync(Guid id)`
- `GetUpcomingAsync(int withinDays)`
- `TryCreateAsync(CreateRecurringTransactionRequest)`
- `TryUpdateAsync(Guid, UpdateRecurringTransactionRequest)`
- `TryDeactivateAsync(Guid)`
- `TryReactivateAsync(Guid)`  ← NEW
- `TryConfirmAsync(Guid, ConfirmRecurringTransactionRequest)`
- `TryDismissAsync(Guid, DateOnly?)`  ← signature gains `nextDueDate`

Drop:
- `CreateAsync(RecurringTransactionCreateViewModel vm)`
- `UpdateAsync(RecurringTransactionEditViewModel vm)`
- `DeactivateAsync(Guid)` (throwing)
- `ConfirmAsync(Guid, DateOnly, decimal, string?, DateOnly?)` (throwing — Razor controller used it)
- `DismissAsync(Guid)` (throwing)

The plan-writing step audits for any other throwing variants by grepping the service file.

**Razor views deleted:** all 8 (`Index`, `Create`, `Edit`, `Confirm`, `Dismiss`, `Deactivate`, `Upcoming`, `_ReminderTable`).

**ViewModels deleted:** `RecurringTransactionCreateViewModel`, `RecurringTransactionEditViewModel`.

**Test audits:**
- `RecurringTransactionServiceTests.cs` — drop tests against deleted throwing methods. Keep all the AdvanceDueDate / Snap behaviour tests. Add new tests per §8a.
- `UserIdStampingTests.cs` — drop `RazorRecurringTransactionService_*_stamps_UserId` tests if they exist (Categories/Accounts precedent).
- `UiVerificationTests.cs:178, 206, 221` — Razor view smoke tests. **Delete the recurring entries** since the views no longer exist.

---

## 9. Topbar bell wiring

### `ReminderCountProvider`

```tsx
type Ctx = {
  count: number;
  reminders: RecurringTransactionListItemDto[];
  loading: boolean;
  refresh: () => void;
};

export const ReminderCountContext = createContext<Ctx>(/* defaults */);

export function ReminderCountProvider({ children }) {
  const { data, loading, refetch } = useApi<RecurringTransactionListItemDto[]>(
    '/api/recurring-transactions/upcoming?days=0'
  );
  const value = useMemo(() => ({
    count: data?.length ?? 0,
    reminders: data ?? [],
    loading,
    refresh: refetch,
  }), [data, loading, refetch]);
  return <ReminderCountContext.Provider value={value}>{children}</ReminderCountContext.Provider>;
}

export function useReminderCount() {
  return useContext(ReminderCountContext);
}
```

`<ReminderCountProvider>` wraps the app shell in `App.tsx`, inside the auth/router boundary so it has access to whatever fetch context the app uses.

### `NotificationsButton` (rewrite)

```tsx
function NotificationsButton() {
  const { count, reminders, loading, refresh } = useReminderCount();
  const [open, setOpen] = useState(false);

  const handleOpenChange = (next: boolean) => {
    setOpen(next);
    if (next) refresh();
  };

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger render={
        <Button variant="ghost" size="icon" aria-label={count > 0 ? `Notifications, ${count} due` : 'Notifications'}>
          <Bell className="h-5 w-5" />
          {count > 0 && (
            <span className="absolute -top-0.5 -right-0.5 …" aria-hidden>{count > 9 ? '9+' : count}</span>
          )}
        </Button>
      } />
      <PopoverContent align="end" className="w-80 p-0">
        <div className="px-3 py-2 border-b text-sm font-medium">Reminders</div>
        {loading ? (
          /* skeleton */
        ) : reminders.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted-foreground">Nothing due. You're all caught up.</p>
        ) : (
          <ul className="max-h-72 overflow-y-auto">
            {reminders.map(r => <ReminderRow key={r.id} reminder={r} onClick={() => setOpen(false)} />)}
          </ul>
        )}
        <div className="px-3 py-2 border-t">
          <Button variant="link" size="sm" render={
            <Link to="/recurring" onClick={() => setOpen(false)}>View all reminders →</Link>
          } />
        </div>
      </PopoverContent>
    </Popover>
  );
}
```

### Cross-page sync

- After any mutation in `RecurringRowMenu`, `RecurringCreate`, `RecurringEdit`, `RecurringConfirmDialog`, `RecurringDismissDialog` → call `ctx.refetch()` (list) + `useReminderCount().refresh()` (bell). The Outlet context exposes `refreshBell` as a convenience so child pages don't import the context separately.
- The Dashboard's existing `RemindersCard` is **not changed in this commit**. It hits a different endpoint (`/api/dashboard/reminders/upcoming`) and refetches on its own. Logged as future cleanup if cross-card consistency becomes friction.

### Tests added

`ReminderCountProvider.test.tsx`:
- `Provides count from /api/recurring-transactions/upcoming?days=0`
- `refresh() refetches`
- `Empty list yields count=0`

`TopBar.test.tsx` (new tests appended):
- `Bell shows badge when count > 0`
- `Bell shows '9+' when count > 9`
- `Opening the popover refetches`
- `Empty state renders 'Nothing due. You're all caught up.'`
- `Reminder row click closes the popover and links to /recurring`
- `'View all reminders →' link navigates to /recurring`

---

## 10. Tests, verification gate, commit shape

### Client tests (Vitest + React Testing Library, co-located)

| File | Coverage |
|---|---|
| `reminder-status.test.ts` | classifyStatus on overdue / due-today / upcoming / archived; combined manual+overdue case |
| `RecurringTable.test.tsx` | One row per reminder · 6-column layout · status badges in Name cell · em-dash for null estimatedAmount · sort by NextDueDate ascending · archived rows opacity-60 · empty state |
| `RecurringRowMenu.test.tsx` | Active row shows Confirm/Edit/Dismiss/Archive · archived row shows Reactivate only · Confirm opens dialog · Dismiss opens dialog · Archive opens dialog · Reactivate fires PATCH directly + toast · 204 fires success toasts and onChanged · errors fire generic error toast |
| `RecurringConfirmDialog.test.tsx` | Defaults populate from reminder · ManualDate adds Next due date field · Snap/Relative hides it · Submit posts correct body · 201 toast includes "View transaction →" link · 422 DATE_BEFORE_OPENING_BALANCE renders inline error · other errors render generic toast |
| `RecurringDismissDialog.test.tsx` | Snap/Relative shows simple confirm · ManualDate shows Next due date input · 204 toast format · onChanged fires |
| `RecurringForm.test.tsx` | Renders Name/Account/Category/EstimatedAmount/Frequency/Behaviour/NextDueDate · Day of week shown for Snap+Weekly/Biweekly · Day of month shown for Snap+Monthly · No day picker for Snap+Annual · No day picker for Relative or Manual · Empty estimatedAmount sends null · 0 estimatedAmount sends 0 · Cached day value preserved across compatible frequency switches · Save disabled until dirty · Save shows "Saving…" · Inputs retain edits on ok:false · Cancel calls onCancel |
| `RecurringLayout.test.tsx` | Skeleton while loading · 6-column table renders · "+ New reminder" navigates · Search filters rows · Search-empty state with Clear · Include archived adds archived rows · GET error renders CardError · First-run empty state · Active-empty-but-archived state |
| `RecurringCreate.test.tsx` | Renders form when accounts+categories load · POST 2xx success toast + navigate + refetch + refreshBell · POST 422 retains values · 422 INVALID_DAY_OF_PERIOD surfaces generic toast (per-field surfacing deferred) |
| `RecurringEdit.test.tsx` | Renders form pre-populated · GET 404 not-found banner · PATCH success toast "Saved." + navigate · PATCH 422 retains values |
| `ReminderCountProvider.test.tsx` | Provides count · refresh refetches · Empty list yields 0 |
| `TopBar.test.tsx` (additions) | Badge renders/hides correctly · Opening popover refetches · Empty popover content · Reminder row navigation · "View all" navigation |

Estimated total: **~11 test files, ~75 tests.**

### Server tests (xUnit + Moq + FluentAssertions)

| File | Additions / changes |
|---|---|
| `RecurringTransactionPoliciesTests.cs` | NEW. 8 tests per §8b |
| `RecurringTransactionServiceTests.cs` | Drop tests against deleted throwing methods. Add 5 tests per §8a (Weekly/Biweekly Snap fix). Add 3 tests for TryReactivateAsync. Add tests asserting null EstimatedAmount round-trips. |
| `RecurringTransactionsCrudApiTests.cs` | Add 4 Reactivate tests (§8c). Add 4 EstimatedAmount nullability tests (§8d). |
| `UserIdStampingTests.cs` | Drop `RazorRecurringTransactionService_*` tests if present. |
| `UiVerificationTests.cs` | Drop entries 178, 206, 221 (Razor view smoke tests for Recurring). |

### Verification gate (before commit)

1. **`dotnet test`** — all server tests green.
2. **`cd ProjectCeres.Client && pnpm test`** — all client tests green.
3. **`cd ProjectCeres.Client && pnpm build`** — production build green.
4. **Documentation sync** — invoke the `sync-docs` skill against the working diff. Per saved feedback, this is a named gate. Targets:
   - `docs/planning-phase3-spa-migration.md` §2 controller table — flip the `RecurringTransactionsController` row to "Migrated 2026-05-03" with commit hash placeholder, spec link, plan link.
   - `docs/planning-phase3-spa-migration.md` §8 "Frontend execution batches" table — flip Recurring row from "Pending" to "✅ Migrated 2026-05-03 (commit `<sha>`)".
   - `docs/planning-phase3.md` §14 — add the "✓ **Recurring transactions — list + Create/Edit + Confirm/Dismiss/Archive/Reactivate + topbar bell wiring + Weekly/Biweekly Snap fix** (2026-05-03)" bullet.
   - `docs/api-contract.md` — add the new `Reactivate` endpoint to the documented surface; document the EstimatedAmount nullability change.
   - `docs/models.md` — fix Dismiss-advances-schedule drift; clarify EstimatedAmount NULL semantics; add Weekly/Biweekly Snap+DayOfPeriod=1..7 (day-of-week) row.
   - **No new ADR.**
   - **Verify each proposed update against actual file state before applying** — saved feedback `feedback_changelog_verify_before_proposing.md`.
5. **Manual click-through** (user step):
   - Visit `/app/recurring`. List renders 6 columns. Status badges visible. Topbar bell shows count = number of due+overdue rows.
   - Search "rent". Filters to matching rows. Clear search.
   - Toggle Include archived. Archived rows appear with opacity + badge.
   - Click `⋯ → Confirm…` on Rent. Dialog opens with defaults. Edit Amount. Click Confirm. Toast "Recorded €X on YYYY-MM-DD." with "View transaction →" link. Bell badge ticks down. Row's NextDueDate has advanced.
   - Click `⋯ → Dismiss…` on a Snap reminder. Dialog with no NextDueDate field. Click Dismiss. Toast "Dismissed. Next due date advanced to …".
   - Click `⋯ → Dismiss…` on a ManualDate reminder. Dialog requires NextDueDate. Submit. Toast.
   - Click `⋯ → Archive…` on an active reminder. Dialog with consequence copy. Click Archive. Row disappears from active list.
   - Toggle Include archived. Archived row visible with menu = `[Reactivate]` only. Click Reactivate. Row jumps back to active section. Toast "Reactivated."
   - Click `+ New reminder`. Pick Account, Category, Frequency=Weekly, Behaviour=Snap. Day of week field appears. Pick Monday. Switch Frequency to Monthly — Day of week disappears, Day of month appears. Switch Behaviour to ManualDate — both disappear. Save with ManualDate config. Verify row shows `[Manual]` badge.
   - Open topbar bell. Popover lists due+overdue with badges. Click a row → navigates to /recurring. Click "View all" → /recurring.
   - Visit `/RecurringTransactions`. 302 → `/app/recurring`. Same for `/Create`, `/Edit/<guid>`, `/Confirm/<guid>`, `/Dismiss/<guid>`, `/Deactivate/<guid>`, `/Upcoming`.
6. **Commit** — single commit, all changes (code + tests + docs) atomic.

### Commit shape

**Single commit**, same as Categories/Accounts. All work — client + server policy + new endpoint + Snap fix + Razor cutover + tests + doc sync — lands together.

**Commit message scaffold:**

```
feat(spa): Recurring transactions page + topbar bell + Snap fix

Replaces the /app/recurring placeholder with a full Recurring SPA: list page
(search, Include archived, status badges, 6-col table), Create/Edit form
(Frequency-driven day picker — day-of-week for Weekly/Biweekly+Snap,
day-of-month for Monthly+Snap), and AlertDialog flows for Confirm
(creates a transaction + advances NextDueDate), Dismiss (advances
NextDueDate, ManualDate gets a date input), and Archive. Adds Reactivate
endpoint + row action satisfying the 'archive ships with reactivate' rule.

Wires the topbar bell to /api/recurring-transactions/upcoming?days=0,
showing a badge count and a popover list of due+overdue reminders, replacing
the 'no notifications' stub.

Server changes:
- RecurringTransactionService.SnapToCalendarDay now honours DayOfPeriod for
  Weekly+Biweekly (1=Mon..7=Sun, snaps to next occurrence of that weekday).
- New RecurringTransactionPolicies validates DayOfPeriod range vs Frequency
  and Behaviour.
- New PATCH /api/recurring-transactions/:id/reactivate endpoint.
- EstimatedAmount is now properly nullable on Create/Update request DTOs;
  null = "amount varies" (matches dashboard's existing imminentBills filter).

Slims the Razor RecurringTransactionsController to seven 302 redirects.
Deletes 8 Razor views and 2 throwing-only ViewModels.

Spec: docs/superpowers/specs/2026-05-03-recurring-spa-design.md
Plan: docs/superpowers/plans/2026-05-03-recurring-spa.md
```

---

## 11. Out of scope (logged elsewhere)

| Item | Destination | Trigger |
|---|---|---|
| Categories + Accounts reactivate retro-fit | Separate small commit on the same day | After this commit ships |
| Transaction → Reminder FK (provenance) | `docs/planning-future.md` → "Maybe / Future Consideration" | When a user reports difficulty reconciling a recurring expense |
| Background scheduler / push notifications | Already deferred via ADR-0044 | When email service ships in Phase 3 |
| Per-field server-error surfacing on form submit | App-wide concern, not Recurring-specific | When generic toast becomes insufficient |
| Cross-card consistency between topbar bell and dashboard RemindersCard | Logged for follow-up | If users report drift between the two |
| Quarterly / semi-monthly / "every N units" frequencies | `docs/planning-future.md` | When a user requests a frequency outside the current 4 |
| Reminder pause vs archive distinction (currently same thing) | `docs/planning-future.md` | When users report archive feels too final for "pause for one month" intent |

---

## 12. Decision log

11 brainstorm questions answered.

| Q | Topic | Decision |
|---|---|---|
| 1 | List shape | Single list with status badges (Overdue · Due today · Manual · Archived), sorted by NextDueDate ascending |
| 2 | Confirm UX | Inline AlertDialog from row menu, fields editable, ManualDate adds NextDueDate input |
| 3 | Dismiss UX | AlertDialog confirm; ManualDate reminders get a NextDueDate field (server already supports it) |
| 4a | Server fix | Yes — fix Weekly/Biweekly+Snap to honour DayOfPeriod as day-of-week (1=Mon..7=Sun) |
| 4b | Day picker | Conditional picker: day-of-week Popover for Snap+Weekly/Biweekly, day-of-month number input for Snap+Monthly, hidden otherwise |
| 5 | EstimatedAmount nullability | Round-trip null = "varies"; cleanup the Request DTO; align with dashboard's existing filter |
| 6 | Reactivate | Yes — add Recurring's reactivate endpoint here; Categories/Accounts retro-fit deferred to a separate commit |
| 7 | Archived edit | Read-only — no Edit affordance on archived rows. Reactivate first, then edit |
| 8 | Dismiss schedule semantics | Code wins — Dismiss advances NextDueDate. Doc fix in models.md |
| 9 | Upcoming view | No separate route — unified list + status badges + topbar bell + dashboard RemindersCard cover the surface |
| 10 | Topbar bell | Badge count + Popover list + link to /recurring; no inline Confirm/Dismiss in popover |
| 11 | Bell data source | Reuse `/api/recurring-transactions/upcoming?days=0` (no new endpoint) |
| 12 | Bell sync | Provider context with `refresh()`; refetches on popover open and after own-page mutations |
| 13 | List columns | Razor parity — 6 columns including Category; max-w-4xl for the page |
| 14 | Status badges | Overdue · Due today · Manual · Archived; rows can carry both Overdue and Manual |
| 15 | Post-confirm nav | Stay on the list, refetch, toast with "View transaction →" link |
| 16 | Transaction FK | Defer — toast link uses returned transactionId; FK is separate scope |

### Reasoning for the load-bearing calls

- **Q4a — fixing Weekly/Biweekly Snap:** the current server code silently drops DayOfPeriod for these frequencies, which makes Weekly+Snap+"every Monday" impossible to express. Fixing it costs one helper method (~15 lines), 5 tests, and a policy clause. Shipping the SPA without the fix would mean rendering a day-of-week picker that the server ignores — worse than not rendering it.
- **Q5 — null EstimatedAmount:** the dashboard's `imminentBills` query already skips nulls (`DashboardService.cs:219`), so the dashboard semantics are correct already; the only thing wrong is that the API's Request DTO doesn't accept null today, forcing the SPA to send 0 instead. The fix is dropping `Range`'s lower bound coupling on a non-nullable decimal — a 1-line DTO change.
- **Q6 — Reactivate scope:** your saved feedback says "Archive always ships with Reactivate." Adding it to Recurring is small (~30 lines server + 1 test file + 1 row menu item). Retro-fitting Categories+Accounts in the same commit triples the scope and risks rebase conflicts; separating into a follow-up commit is cleaner. The retro-fit itself is logged as a clear next step.
- **Q8 — Dismiss semantics:** without advancing the schedule, Dismiss is a no-op that produces an infinite "due-now" loop. The doc is wrong; the code is right; we fix the doc.
- **Q11 — Reuse `/upcoming?days=0`:** the existing endpoint already does the exact filter. A second "due" endpoint is purely cosmetic and adds maintenance surface. Reuse wins until or unless the count query needs to diverge.

### Items the brainstorm explicitly considered and rejected

- Three-section page (Due/Upcoming/Inactive) like Razor — too much chrome; status badges + sort give the same signal in less space.
- Tabs (All / Due / Upcoming) — Categories/Accounts explicitly avoided tabs; consistency wins.
- Quick Confirm with no dialog — fast for the common case but breaks for ManualDate (NextDueDate required) and removes the user's ability to adjust Amount/Description.
- Dedicated `/recurring/:id/confirm` route — full-page navigation per Confirm is heavy for a frequent action.
- Always-visible day-of-period field with disabled state — soft-hide violates the "render only what matters" direction the prior batches took.
- New `/api/recurring-transactions/due` endpoint — pure cosmetic redundancy with `/upcoming?days=0`.
- Inline Confirm/Dismiss buttons inside the bell popover — duplicates AlertDialog wiring across two places; row menu is enough.
- Adding `Transaction.RecurringTransactionId` FK in this commit — touches Movements, requires migration, can be added later when a real reconciliation use case shows up.

---

**End of plan.**
