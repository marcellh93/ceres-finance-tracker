# Budgets — SPA Surface (Phase 3 step 5)

> **Status:** Brainstormed and locked 2026-05-01. Ready for implementation planning.
> **Phase 3 step:** 5 of 7 (per `docs/planning-phase3-spa-migration.md` §8 sequencing).

This spec covers the SPA migration of the Budgets surface — both Category Budgets (per-month spending caps) and Goal Budgets (named Spending or Savings targets). It also implements the previously-deferred `Settings.BudgetPeriodStartDay` setting, since the dashboard cycle math depends on it once a real management surface exists.

The Razor `BudgetsController` and its eight `.cshtml` views are demolished in the same plan that ships the SPA replacement, matching the migration playbook used for Movements.

---

## Index

1. [Page surface](#1-page-surface)
2. [Routes](#2-routes)
3. [List rendering](#3-list-rendering)
4. [Forms](#4-forms)
5. [Period semantics — `Settings.BudgetPeriodStartDay`](#5-period-semantics--settingsbudgetperiodstartday)
6. [Archive lifecycle](#6-archive-lifecycle)
7. [Spending Goal tagging on Movements](#7-spending-goal-tagging-on-movements)
8. [API endpoints](#8-api-endpoints)
9. [Razor demolition](#9-razor-demolition)
10. [Out of scope](#10-out-of-scope)
11. [Open question — period semantics for non-current periods](#11-open-question--period-semantics-for-non-current-periods)

---

## 1. Page surface

A single `/app/budgets` route, served by one `<BudgetsLayout>` page. Inside the page, a horizontal tab strip toggles between two lists:

- **Category Budgets** — per-category monthly spending caps.
- **Goal Budgets** — named Spending or Savings targets.

The tabs share the page header with a `+ New ▾` dropdown (three items — see §1.1 below) and a "Show archived" toggle. URL query parameters drive both controls so back/forward and bookmarks work:

- `?type=category` (default) or `?type=goal` selects the active tab.
- `?includeArchived=true` (omitted = false) shows archived rows alongside active.

**Why one page, not two:** the user has a single sidebar entry "Budgets." Splitting into `/budgets/categories` and `/budgets/goals` (the original `planning-phase3-spa-migration.md` §5 route map) introduces a hidden second page from a single nav entry. Tabs make the choice visible the moment the page loads.

**Why no currency tabs (unlike Movements):** Budgets carry their currency intrinsically — the row's Limit/Target is denominated in a specific currency, and the symbol is rendered prominently per row. Mixing currencies in the list is informational, not confusing the way a mixed Amount column on Movements was. If, later, a user has dozens of budgets across multiple currencies and the list becomes noisy, a currency filter dropdown can be added without restructuring.

### 1.1. The `+ New` dropdown

Three items, mirroring the Movements `+ New` pattern:

- **Category Budget** → `/app/budgets/new?type=category` (and switches the page to the Category tab on return).
- **Spending Goal** → `/app/budgets/new?type=spending` (and switches to the Goal tab on return).
- **Savings Goal** → `/app/budgets/new?type=savings` (and switches to the Goal tab on return).

Each item carries a tooltip explaining what it is (same pattern as the Movements dropdown after the rename of "Liability Payment" → "Debt Payment"). Suggested copy:

| Item | Tooltip |
|---|---|
| Category Budget | "A monthly cap on spending in a specific category (e.g., €600/month on groceries)." |
| Spending Goal | "Track money you're spending toward a target (e.g., a trip, a renovation). Tagged transactions count toward progress." |
| Savings Goal | "Track money accumulated in a designated account (e.g., emergency fund). Progress = current account balance." |

---

## 2. Routes

```
/app/budgets                        list page (tabbed)
/app/budgets/new                    routed Create page (?type=category|spending|savings)
/app/budgets/:id/edit               routed Edit page (discriminator endpoint resolves kind)
```

Matches the `/app/movements` precedent exactly. The Edit page first hits `GET /api/budgets/{id}` to discover whether the id is a `CategoryBudget` or a `GoalBudget`, then loads the typed edit DTO from the correct endpoint.

The `?type=` URL param on `/budgets` selects the tab; the `?type=` URL param on `/budgets/new` selects the form variant. They are independent — a user landing on `/budgets/new?type=spending` from any starting tab gets the Spending Goal form.

---

## 3. List rendering

Both lists share visual chrome (table card, row spacing, menu component, hover state). They differ in columns.

### 3.1. Category Budgets table

| Column | Content |
|---|---|
| Category | The category name (e.g., "Groceries") |
| Currency | Symbol + 3-letter code (e.g., "€ EUR") |
| This period | `Spent / Limit` formatted per `Settings.NumberFormat` (e.g., "€450,00 / €600,00") |
| Progress | A progress bar — see §3.3 below |
| ⋯ menu | Edit · Archive (or Reactivate if archived) |

Sort order: Category name ascending. Archived rows render with reduced opacity and a small `Archived` badge to the left of the Category name when `includeArchived=true`.

### 3.2. Goal Budgets table

| Column | Content |
|---|---|
| Name | The user-given name (e.g., "Trip to Japan") |
| Type | `Spending` or `Savings` badge (use chart-color tokens consistent with Movements badges) |
| Currency | Symbol + 3-letter code |
| Target | Limit amount, formatted per `Settings.NumberFormat` |
| Progress | `<amount> / <target>` plus a progress bar |
| End date | Formatted per `Settings.DateFormat`, or `—` if `EndDate IS NULL` |
| ⋯ menu | Edit · Archive (or Reactivate if archived) |

Sort order: by `IsActive` desc (active first), then by `EndDate` ascending with NULLs last (urgent goals near top, open-ended at the bottom).

### 3.3. Progress bar component

Already exists in the dashboard's `CategoryBudgetBars` and `GoalBudgetBars` components. Extracted into a shared primitive `<BudgetProgressBar>` so both the dashboard and the Budgets management page render identically. The component accepts `{ progress, target, currencySymbol, numberFormat }` and emits the standard "spent / limit" line plus a colored bar (green at <70%, amber 70–99%, destructive at ≥100%, matching dashboard behavior).

### 3.4. Archive toggle behavior

A simple `<Switch>` at the top of the page reading "Show archived." Off by default. When turned on:

- The active tab's list query refetches with `?includeArchived=true`.
- Archived rows render alongside active rows, with reduced opacity and the `Archived` badge.
- Archived rows have a "Reactivate" item in the row menu instead of "Archive."

The toggle's state is mirrored to `?includeArchived=true` URL param so bookmarks/back-button persist.

---

## 4. Forms

All three forms share the page chrome (`<MovementForm>`-style heading + helper line + form body + Save/Cancel footer). They differ in fields.

### 4.1. Category Budget

| Field | Behavior |
|---|---|
| Category | `<CategoryCombobox>`, filtered to Expense categories. Categories that already have an active budget for the currently-selected currency are appended with `(budgeted)` suffix (visible warning, not disabled — server enforces the unique constraint as defense-in-depth). |
| Currency | `<CurrencyCombobox>` (existing primitive, or a new lightweight one — TBD at implementation). Selecting a currency re-evaluates the `(budgeted)` flags on the Category list. |
| Limit | Currency-aware decimal input (same `<InputGroup>` + symbol + `numberFormat` handling as the Amount field on `MovementForm`). |
| — | Period-start-day is **not** a per-budget field. It lives in Settings only. |

**Validation:**
- Required: Category, Currency, Limit > 0.
- Server returns 422 with `error.code = "DUPLICATE_BUDGET"` if a Category+Currency unique-constraint violation occurs (active OR archived). Body includes `existingBudgetId` and `existingIsActive` so the client can offer a one-click Reactivate (see §6.3 for the 409 flow).

### 4.2. Spending Goal

| Field | Behavior |
|---|---|
| Name | Plain text input, required. |
| Currency | `<CurrencyCombobox>`, required. |
| Target | Currency-aware decimal input, must be > 0. |
| Start date | Date input, required, pre-filled with today. No past/future restriction. |
| End date | Date input, optional. Empty = open-ended goal. |
| Description | Optional textarea. |

**Validation:**
- If `EndDate` is set, must be `≥ StartDate`. Server 422; client renders inline error on the End-date field.

### 4.3. Savings Goal

| Field | Behavior |
|---|---|
| Name | Plain text input, required. |
| Linked account | `<AccountCombobox>`, filtered to Asset-type active accounts. Required. The goal's currency is inferred from the linked account — no separate currency picker. |
| Target | Currency-aware decimal input, must be > 0. The currency symbol comes from the selected account. |
| Start date | Same as Spending Goal: required, pre-fill today, no restriction. |
| End date | Optional, same as Spending Goal. |
| Description | Optional textarea. |

**Validation:**
- Linked account must be Asset-type (not Liability). Server enforces.
- `EndDate ≥ StartDate` if set.

---

## 5. Period semantics — `Settings.BudgetPeriodStartDay`

This implements the previously-deferred Phase 3 plan in `docs/models.md`. The `CategoryBudget.PeriodStartDay` per-budget override is **not** in scope for this iteration — it can be added later without breaking the global default.

### 5.1. Schema

Add a column to the `Settings` table:

```
BudgetPeriodStartDay  int  NOT NULL  DEFAULT 1
```

Range: 1–31. EF Core migration adds the column with `DEFAULT 1` so existing rows behave exactly as today (calendar months) until changed.

The corresponding `CategoryBudget.PeriodStartDay` column described in `docs/models.md` is **not** added in this spec. The `models.md` entry for that column should be updated to either remove it or annotate it as "deferred to a follow-up plan."

### 5.2. Period boundary rule

For a given calendar moment (e.g., today) and a configured `BudgetPeriodStartDay = N`:

1. The **start day for a month M** is `min(N, last day of month M)`. So `N=31` in February falls back to Feb 28 (or Feb 29 in a leap year).
2. The **current period start** is the most recent month-M's start day that is on or before today.
3. The **current period end** is `(next month's start day) − 1 day`.
4. The **period name** is the calendar month containing the period's end date.

#### Worked examples

`BudgetPeriodStartDay = 25`, today = 2026-05-01:
- Most recent valid 25th on or before today: 2026-04-25.
- Next valid 25th after that: 2026-05-25.
- Period start = 2026-04-25, period end = 2026-05-24.
- Period name: **May 2026** (end falls in May).

`BudgetPeriodStartDay = 31`, today = 2026-04-10:
- March's start day = 2026-03-31 (March has 31).
- April's start day = 2026-04-30 (fallback — April has no 31st).
- Today (2026-04-10) is between 2026-03-31 and 2026-04-29 (April-30 minus 1).
- Period start = 2026-03-31, period end = 2026-04-29.
- Period name: **April 2026**.

`BudgetPeriodStartDay = 31`, today = 2026-05-10:
- April's start day = 2026-04-30 (fallback).
- May's start day = 2026-05-31.
- Today (2026-05-10) is between 2026-04-30 and 2026-05-30 (May-31 minus 1).
- Period start = 2026-04-30, period end = 2026-05-30.
- Period name: **May 2026**.

Period lengths vary (28–31 days). This is acceptable — it mirrors how real bank statement cycles work.

### 5.3. Service signature

`ICategoryBudgetService.GetActualSpendAsync(Guid id, int year, int month)` keeps its existing signature. The semantics shift: `(year, month)` now identifies the period whose **end** falls in that calendar month.

A pure helper centralizes the period math so callers don't reinvent it:

```csharp
public static class BudgetPeriod
{
    /// Given a target period-name (year, month) and the configured start day,
    /// returns the period's [start, end] date range per §5.2.
    public static (DateOnly Start, DateOnly End) GetBoundsForMonth(int year, int month, int startDay);

    /// Given today and the configured start day, returns the (year, month)
    /// of the *current* period — the period whose end falls in that month.
    /// Callers pass this back into GetActualSpendAsync.
    public static (int Year, int Month) GetCurrentPeriodMonth(DateOnly today, int startDay);
}
```

`GetActualSpendAsync` implementation:
1. Read `Settings.BudgetPeriodStartDay` (call it `N`).
2. `(periodStart, periodEnd) = BudgetPeriod.GetBoundsForMonth(year, month, N)`.
3. Sum `Transaction.Amount` where `Date BETWEEN periodStart AND periodEnd` AND the transaction's account currency matches the budget's currency AND the transaction's category matches the budget's category.

Dashboard callers update from:

```csharp
var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, now.Year, now.Month);
```

to:

```csharp
var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(today, settings.BudgetPeriodStartDay);
var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, year, month);
```

The helper is a pure function with no DB dependency — unit-test it against every example in §5.2 plus leap-year boundaries (Feb 29 fallback for `startDay = 30` or `31`).

### 5.4. Open question on non-current period semantics

The `(year, month)` signature for `GetActualSpendAsync` previously meant "the calendar month." Tests and any future Reports work that pass historical (year, month) values now interpret them as period-name months. This is a behavior change. Spec §11 below tracks this as an open question — confirm test fixtures and historical-period UX before implementation.

### 5.5. Settings UI

Add a fourth field to `/app/settings`, after Default Currency:

- Label: **Budget period start day**
- Helper text below the input: "All your budget cycles start on this day. Use 1 for calendar months. If a month doesn't have this day (e.g., 31 in April), the cycle starts on that month's last day instead."
- Input: number stepper, min=1, max=31, default=1.
- Save behavior: optimistic write to the existing `PATCH /api/settings` endpoint; on success, `toast.success` reads "Saved. Budget periods now start on the {N}th." (Use English ordinal: 1st / 2nd / 3rd / 4th–9th / 10th / 11th / 12th / 13th–19th / 20th / 21st / 22nd / 23rd / 24th–29th / 30th / 31st.)

**Implication for already-rendered budget progress:** when the user changes the start day, every CategoryBudget's "current period" shifts. The dashboard refetches its budget cards on next navigation. No invalidation flicker is needed within Settings — the user expects to see the change reflected next time they look.

---

## 6. Archive lifecycle

User-facing label: **Archive** (not Deactivate). The data layer continues to use `IsActive = false`; this is a label-only translation in the SPA. No DB or service rename.

### 6.1. Archiving

Each row in either list has a row menu (`⋯`) with an **Archive** action. Clicking opens a small `AlertDialog`:

- Title: "Archive this {type}?" — where {type} is "category budget" / "spending goal" / "savings goal".
- Description: "It will be hidden from the active list but historical data is preserved. You can reactivate it later."
- Footer: Cancel · Archive.

On confirm:
- Client → `PATCH /api/category-budgets/{id}/archive` (or `/api/goal-budgets/{id}/archive`).
- Server → sets `IsActive = false`, returns 204.
- Client → toast "Archived." and refetches the list.

### 6.2. Show archived + reactivate

The `Show archived` toggle (off by default) flips `?includeArchived=true`. When on:

- Archived rows appear alongside active, with reduced opacity (`opacity-60`) and an `Archived` badge.
- Their row menu shows **Reactivate** instead of Archive.
- Reactivate is one click — no confirmation dialog (low blast radius, easily reversible).
- On success, toast "Reactivated." and refetch.

### 6.3. Duplicate handling on Create

The `CategoryBudget` model already enforces `UNIQUE (CategoryId, CurrencyId) WHERE IsActive = true`. We extend the SPA experience to handle archived duplicates explicitly:

When the user submits a Create with a Category+Currency that has an existing record (active OR archived):

- Server returns **409 Conflict** with body:
  ```json
  {
    "error": {
      "code": "DUPLICATE_BUDGET",
      "message": "A budget for this category and currency already exists.",
      "existingBudgetId": "uuid-here",
      "existingIsActive": false
    }
  }
  ```
- Client renders a contextual prompt below the form (NOT a toast):
  - If `existingIsActive = true`: "This combination already has an active budget. [Edit it instead] or [pick another category]."
  - If `existingIsActive = false`: "An archived budget for this category and currency exists. [Reactivate it] or [pick another category]."
- The "[Reactivate it]" link calls `PATCH /api/category-budgets/{existingBudgetId}/reactivate` then navigates to `/budgets/{existingBudgetId}/edit` so the user can adjust the limit before saving.

This 409 flow only applies to Category Budgets — Goal Budgets are unique by `Id` only, no business-key uniqueness constraint.

---

## 7. Spending Goal tagging on Movements

The `Transaction.BudgetId` FK already exists in the model. The Movement form's `MovementFormValues` type already carries `budgetId: string | null`. The current Movement form submits it as `null` always — this spec adds the picker that populates it.

### 7.1. Conditional rendering

A new `Field` rendered between Category and Description on the Transaction variant of `MovementForm`:

- The field is **only rendered** if there's at least one matching active Spending Goal in the transaction's currency. Zero matching goals → the field is hidden entirely (no empty dropdown, no placeholder, no clutter).
- Match criteria:
  - `Budget.GoalType = 'Spending'`
  - `Budget.IsActive = true`
  - `Budget.CurrencyId = (selected account's currency id)`
- Currency derives from `values.accountId` (the selected account on the Transaction). When the user changes the account to one in a different currency, the Budget picker re-evaluates. If the new currency has no matching Spending Goals, the field disappears; if the previously-selected goal is no longer valid, the field's value is cleared.

### 7.2. Edit-mode handling for archived links

If a transaction was previously tagged to a goal that has since been archived (`IsActive = false`), the picker on Edit must:

- Still render the field (even if no other active goals match).
- Show the archived goal in the dropdown, suffixed `(archived)`.
- Allow the user to keep the link, change to an active goal, or clear the link (None option).

This preserves the "FK on this row is preserved when the linked Budget is deactivated" rule from `docs/models.md`.

### 7.3. Where this is wired

- `MovementForm.tsx`: render the conditional field on the Transaction variant only (Transfer and LiabilityPayment do not have `budgetId`).
- `MovementCreate.tsx` and `MovementEdit.tsx`: include `budgetId` in the request body sent to `POST /api/transactions` and `PUT /api/transactions/{id}`. Server-side, the existing `CreateTransactionRequest` / `UpdateTransactionRequest` DTOs already include `BudgetId` — no API contract change.
- `QuickAddModal.tsx`: **not** changed. Quick-add stays minimal; tagging is a deliberate decision better suited to the full form.

### 7.4. New endpoint for the picker data

The picker fetches available Spending Goals via the goal-budgets list endpoint:

```
GET /api/goal-budgets?type=spending&currency=EUR&includeArchived=false
```

Response: lightweight DTOs (`{ id, name, currencyCode }` only — no progress calculation needed for picker rows). Reused for both the Movement form picker and the Goal Budgets list page (which requests the heavier shape).

---

## 8. API endpoints

Two new controllers (`CategoryBudgetsApiController`, `GoalBudgetsApiController`), plus one discriminator endpoint that lives in a new `BudgetsApiController` (single-purpose).

### 8.1. Category Budgets

| Method | Path | Body | Response |
|---|---|---|---|
| `GET` | `/api/category-budgets?currency=&includeArchived=` | — | `200` array of `CategoryBudgetListItemDto` |
| `POST` | `/api/category-budgets` | `CreateCategoryBudgetRequest` | `201 { id }` · `409 DUPLICATE_BUDGET` (see §6.3) · `422 VALIDATION_ERROR` |
| `GET` | `/api/category-budgets/{id}` | — | `200 CategoryBudgetEditDto` · `404` |
| `PUT` | `/api/category-budgets/{id}` | `UpdateCategoryBudgetRequest` | `204` · `404` · `422` |
| `PATCH` | `/api/category-budgets/{id}/archive` | — | `204` · `404` |
| `PATCH` | `/api/category-budgets/{id}/reactivate` | — | `204` · `404` · `409 DUPLICATE_BUDGET` (if reactivating would violate the unique constraint — i.e., another active budget for the same Category+Currency exists) |
| `GET` | `/api/category-budgets/{id}/spend?year=&month=` | — | `200 { spent: decimal, periodStart: date, periodEnd: date }` · `404` |

**DTOs:**

```typescript
// list response
type CategoryBudgetListItemDto = {
  id: string;
  categoryId: string;
  categoryName: string;
  currencyCode: string;     // e.g. "EUR"
  currencySymbol: string;   // e.g. "€"
  limitAmount: number;
  isActive: boolean;
  // The following two are computed for the *current* period (per Settings.BudgetPeriodStartDay):
  currentPeriodSpend: number;
  currentPeriodEnd: string; // yyyy-MM-dd
};

type CreateCategoryBudgetRequest = {
  categoryId: string;
  currencyId: number;
  limitAmount: number;
};

type UpdateCategoryBudgetRequest = {
  categoryId: string;
  currencyId: number;
  limitAmount: number;
  isActive: boolean;
};
```

The list response includes current-period spend so the page can render progress bars without N+1 fetches.

### 8.2. Goal Budgets

| Method | Path | Body | Response |
|---|---|---|---|
| `GET` | `/api/goal-budgets?currency=&type=&includeArchived=` | — | `200` array of `GoalBudgetListItemDto` |
| `POST` | `/api/goal-budgets` | `CreateGoalBudgetRequest` | `201 { id }` · `422` |
| `GET` | `/api/goal-budgets/{id}` | — | `200 GoalBudgetEditDto` · `404` |
| `PUT` | `/api/goal-budgets/{id}` | `UpdateGoalBudgetRequest` | `204` · `404` · `422` |
| `PATCH` | `/api/goal-budgets/{id}/archive` | — | `204` · `404` |
| `PATCH` | `/api/goal-budgets/{id}/reactivate` | — | `204` · `404` |
| `GET` | `/api/goal-budgets/{id}/progress` | — | `200 { progress: decimal, target: decimal, percentage: int }` · `404` |

The `?type=` filter accepts `spending` or `savings` (lowercase, matches existing convention from `?type=transaction|transfer|liabilitypayment` on Movements).

**DTOs:**

```typescript
type GoalBudgetListItemDto = {
  id: string;
  name: string;
  goalType: 'Spending' | 'Savings';
  currencyCode: string;
  currencySymbol: string;
  targetAmount: number;
  startDate: string;        // yyyy-MM-dd
  endDate: string | null;   // yyyy-MM-dd or null
  description: string | null;
  isActive: boolean;
  linkedAccountId: string | null;     // populated only for Savings
  linkedAccountName: string | null;
  // Computed progress (full-lifetime for Spending, current balance for Savings):
  progress: number;
};

type CreateGoalBudgetRequest = {
  name: string;
  goalType: 'Spending' | 'Savings';
  currencyId: number | null;     // required if Spending; null if Savings (inferred)
  targetAmount: number;
  startDate: string;             // yyyy-MM-dd
  endDate: string | null;
  description: string | null;
  linkedAccountId: string | null; // required if Savings; null if Spending
};
```

Server-side, `CreateGoalBudgetRequest` validates the goal-type-specific rules:
- `goalType = 'Spending'` → `linkedAccountId` MUST be null, `currencyId` MUST be set.
- `goalType = 'Savings'` → `linkedAccountId` MUST be set (Asset-type account), `currencyId` MUST be null (will be derived from the linked account on the server).
- `endDate` if set must be ≥ `startDate`.

### 8.3. Discriminator endpoint

```
GET /api/budgets/{id}    →    200 { id: string, kind: 'CategoryBudget' | 'GoalBudget' }
                              404 if id matches neither table
```

Used by `/app/budgets/:id/edit` to decide which typed endpoint to fetch. No POST/PUT/DELETE on this URL — it's read-only routing metadata.

### 8.4. Endpoint count

15 endpoints across 3 controllers (CategoryBudgets: 7, GoalBudgets: 7, Budgets discriminator: 1).

---

## 9. Razor demolition

Same playbook as the 2026-04-30 Movements migration. Performed in the same plan that ships the SPA replacement.

### 9.1. `BudgetsController.cs`

All page-rendering actions become 302 redirects to `/app/budgets`:

- `Index()` (GET) → `Redirect("/app/budgets?type=category")`
- `Goals()` (GET) → `Redirect("/app/budgets?type=goal")`
- `Create()` (GET) → `Redirect("/app/budgets/new?type=category")`
- `CreateGoal()` (GET) → `Redirect("/app/budgets/new?type=spending")` (defaulting to Spending; the SPA form has no in-page type-switch, so users wanting a Savings Goal navigate via the SPA's `+ New ▾` dropdown instead. The legacy URL didn't distinguish Spending vs. Savings either, so this is no regression.)
- `Edit(Guid)` (GET) → `Redirect($"/app/budgets/{id}/edit")`
- `EditGoal(Guid)` (GET) → `Redirect($"/app/budgets/{id}/edit")`
- `Deactivate(Guid)` (GET) → `Redirect($"/app/budgets/{id}/edit")` (Edit page hosts the danger-zone Archive action)
- `DeactivateGoal(Guid)` (GET) → `Redirect($"/app/budgets/{id}/edit")`

All POST overloads (`Create(POST)`, `CreateGoal(POST)`, `Edit(POST)`, `EditGoal(POST)`, `Deactivate(POST)`, `DeactivateGoal(POST)`) → **deleted entirely**. The SPA POSTs JSON to the new API. No `[Obsolete]` retention is needed — there are no bulk endpoints or toggle endpoints to keep.

After the trim, `BudgetsController` should be a slim file with eight 302-only GET actions. The DI services (`ICategoryBudgetService`, `IBudgetService`, `AppDbContext`) are no longer used and are dropped from the constructor.

### 9.2. Razor views

Delete all eight files under `Views/Budgets/`:

- `Index.cshtml`
- `Goals.cshtml`
- `Create.cshtml`
- `CreateGoal.cshtml`
- `Edit.cshtml`
- `EditGoal.cshtml`
- `Deactivate.cshtml`
- `DeactivateGoal.cshtml`

If any other Razor views reference `Budgets/*` actions via `asp-controller="Budgets" asp-action="Index"`, the 302 will resolve them to the SPA — leave those references alone (per the Movements migration playbook).

### 9.3. Inbound link audit

At implementation kickoff, grep for `asp-controller="Budgets"` and inline `/Budgets/` URLs in all remaining Razor views (`_Layout.cshtml`, `Dashboard/*`, etc.). Document each call site in the implementation plan but do NOT change them — the 302s catch them.

---

## 10. Out of scope

Documented as known gaps so they aren't quietly forgotten:

- **Per-budget `CategoryBudget.PeriodStartDay` override.** The model docs describe this as a Phase 3 column. This spec implements only the global `Settings.BudgetPeriodStartDay`. The per-budget override can be added later without breaking the global default.
- **Period concept on Goal Budgets.** Goals use absolute `StartDate` / `EndDate` and full-lifetime progress; periods are not applicable. Not changing.
- **Auth.** Per `docs/planning-phase3-spa-migration.md` §8, auth ships in the final feature group. Budgets ship unauthenticated for now, matching the rest of the SPA surface.
- **Budget vs Actual report.** Out of scope; lives in the Reports plan (Phase 3 step 5b, after this).
- **Bulk archiving / reactivation.** No bulk operations on budgets in this spec. Single-row only.
- **Spending Goal tagging in QuickAddModal.** Quick-add stays minimal; tagging is full-form only.
- **(Note: doc-sync tasks for `models.md`, `api-contract.md`, `planning-phase3.md`, `planning-phase3-spa-migration.md`, and `CHANGELOG.md` ARE in scope — they're listed in the Appendix below as part of the implementation plan, not deferred.)**

---

## 11. Open question — period semantics for non-current periods

The `(year, month)` signature for `GetActualSpendAsync` previously meant "the calendar month." After this change it means "the period whose end falls in (year, month)." This is a semantic shift.

- All current callers pass `now.Year` / `now.Month`. After this change, those need to be replaced with the helper `GetCurrentPeriodEndMonth(today, settings.BudgetPeriodStartDay)` that returns the period-name (year, month).
- Any test fixtures that seed transactions into a calendar month and assert a budget's actual spend MAY break, because (year=2026, month=4) no longer means "April 2026 calendar" — it means "the period ending in April 2026."

The spec assumes `BudgetPeriodStartDay = 1` keeps existing tests green (because period = calendar month when start day = 1). The implementation plan must verify this and update any tests that pass a non-default start day or assume calendar-month semantics with `start day = 1`.

If a future Reports surface needs arbitrary date-range queries (e.g., "Q1 2026 spend across all budgets"), we add a separate method `GetActualSpendForRangeAsync(id, start, end)` rather than overloading the (year, month) signature.

---

## Appendix — relationship to existing planning docs

- `docs/planning-phase3-spa-migration.md` §2 "Controller-by-controller porting map" → update `BudgetsController` row to **Migrated (2026-MM-DD)** when this work ships.
- `docs/planning-phase3-spa-migration.md` §5 "React Router route map" → replace `/budgets/categories` and `/budgets/goals` with the unified `/budgets`, `/budgets/new`, `/budgets/:id/edit` routes.
- `docs/planning-phase3.md` §14 step 5 ("Budgets + Reports") → mark Budgets ✓ when shipped.
- `docs/models.md` → update `CategoryBudget.PeriodStartDay` row to either remove it (deferred) or annotate "deferred to a follow-up plan." Update `Settings.BudgetPeriodStartDay` row (already exists) to remove the "Phase 3" annotation and confirm it's now implemented. Confirm range docs read 1–31 (currently 1–28).
- `docs/api-contract.md` → add the 15 new endpoints under their respective sections.
- `CHANGELOG.md` → entry under `[Unreleased]` summarizing the Budgets CRUD slice.
