# Spec: Dashboard — Phase 1 (4 sections)

> **Date:** 2026-04-29
> **Phase:** 3
> **Status:** Approved — ready for implementation planning
> **Predecessors:** [App Shell](2026-04-29-app-shell-design.md), [Design System Foundation](../plans/2026-04-29-design-system-foundation.md)

The first React Dashboard plan. Ports four sections to the SPA shell at `/app/` (the index route): Financial Health, a 3-up KPI strip (Net Worth + MTD + Reminders), Category Budgets, and Goal Budgets. The remaining six sections of the existing Razor Dashboard (charts, account balances, cash flow, etc.) land in a follow-up plan that also performs the Razor view deletion and adds the migration redirect.

This is the first **feature page** to live inside the App Shell. It exercises the design system in real context, establishes the per-feature file pattern under `src/app/features/`, and introduces the `useApi` data-fetching hook used by every subsequent SPA page.

---

## Index

1. [Architecture](#1-architecture)
2. [Layout](#2-layout)
3. [Component contracts](#3-component-contracts)
4. [Backend audit and `/api/dashboard/summary`](#4-backend-audit-and-apidashboardsummary)
5. [Testing](#5-testing)
6. [Out of scope](#6-out-of-scope)

---

## 1. Architecture

### Scope

This plan ships four widgets at `/app/` (the SPA shell's index route, mapped to the `Dashboard` page component). Both routes work in parallel during the gap: the existing `/Dashboard` Razor view stays untouched on disk and continues to render the full original dashboard. The new SPA Dashboard at `/app/` shows only these four sections.

The 302 redirect from `/Dashboard` → `/app/` and the deletion of the Razor view + `_HealthSnapshot.cshtml` partial land in the *next* Dashboard plan, when the remaining six sections are ported. The redirect rule itself is documented in [`planning-phase3-spa-migration.md`](../../planning-phase3-spa-migration.md#per-area-redirect-rules-during-migration).

### File layout

A new `features/` directory alongside the existing `pages/`, `layout/`, `components/`, `lib/`. Convention going forward: per-feature React components and their fetchers live under `features/<feature-name>/`. `pages/` is the route entry that *composes* features.

```
src/app/
├── pages/
│   └── Dashboard.tsx                         # replaces the placeholder; composes the 4 sections
├── features/
│   └── dashboard/
│       ├── FinancialHealthCard.tsx           # full-width header section
│       ├── KpiStrip.tsx                      # 3-up row container
│       ├── NetWorthCard.tsx                  # KPI 1
│       ├── MtdCard.tsx                       # KPI 2 (Month to Date)
│       ├── RemindersCard.tsx                 # KPI 3
│       ├── CategoryBudgetsCard.tsx           # full-width below
│       ├── GoalBudgetsCard.tsx               # paired with Category on lg+
│       ├── *.test.tsx                        # one test file per component
│       └── api.ts                            # typed fetchers + DTOs for /api/dashboard/*
└── lib/
    └── use-api.ts                            # NEW — loading/data/error/refetch hook
```

### Reusing existing React components

Two existing React components are wrapped (not rewritten) inside our new card chrome:

- `ProjectCeres.Client/src/components/CategoryBudgetBars.tsx` — kept as-is; consumed by `CategoryBudgetsCard`.
- `ProjectCeres.Client/src/components/GoalBudgetBars.tsx` — kept as-is; consumed by `GoalBudgetsCard`.

These components currently mount as Razor islands via `data-react=` attributes in the existing Dashboard view. They continue to work in the Razor Dashboard untouched. The wrapping in this plan is additive — we do not rip them out of the island setup.

`NetWorthChart`, `IncomeExpenseChart`, `SpendingByCategoryChart`, `AccountBalancesChart`, `CashFlowChart` are NOT in this plan — they belong to the follow-up dashboard plan.

### Data fetching

A single new hook `useApi` at `src/app/lib/use-api.ts` wraps `fetch` and returns `{ data, error, loading, refetch }`. Used by every dashboard card. ~30 lines, no external dependencies.

This is the standard pattern for the SPA until TanStack Query is adopted (criteria for adoption documented in [`planning-future.md`](../../planning-future.md#tanstack-query-server-data-caching-layer)). The migration path is: introduce `QueryClientProvider` at the root, replace `useApi` call sites with `useQuery` one page at a time. The two patterns can coexist during migration.

### API endpoints

Four endpoints feed the page:

- `GET /api/dashboard/health` — exists. Financial Health snapshot.
- `GET /api/dashboard/category-budgets` — exists. Used by `CategoryBudgetBars`.
- `GET /api/dashboard/goal-budgets` — exists. Used by `GoalBudgetBars`.
- `GET /api/dashboard/summary` — **new in this plan.** Bundles Net Worth, MTD income/expenses/savings rate, and pending reminders count into a single response. The data is currently only computed in the Razor `DashboardController.Index()` for its `DashboardData` ViewModel — no API exposure.

See §4 for the new endpoint's full shape.

---

## 2. Layout

### Page structure

```
┌─────────────────────────────────────────────────────────┐
│  Dashboard                              (page heading)  │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  Financial Health                                       │   full width
│    Spendable Balance · Safe to Spend · WarningLevel     │
└─────────────────────────────────────────────────────────┘

┌──────────────────┬──────────────────┬───────────────────┐
│  Net Worth       │  Month to Date   │  Reminders        │   3-up at sm+
│  (per currency)  │  income/exp/rate │  count + link     │
└──────────────────┴──────────────────┴───────────────────┘

┌────────────────────────┬────────────────────────────────┐
│  Category Budgets      │  Goal Budgets                  │   2-up at lg+
│  (View all →)          │  (View all →)                  │
└────────────────────────┴────────────────────────────────┘
```

### Breakpoints

- **Health card:** full width at every breakpoint.
- **KPI strip:** `grid-cols-1 sm:grid-cols-3 gap-6` — single column below 640px (`<sm`), 3-up at 640px and above. Justification: the cards are small; 3-up reads fine on tablets.
- **Budgets row:** `grid-cols-1 lg:grid-cols-2 gap-6` — single column below 1024px (`<lg`), 2-up at 1024px and above. Justification: budget bars need horizontal room; 2-up looks cramped at tablet width.
- **Outer page stack:** vertical, `space-y-6` between sections.

### Implementation sketch

```tsx
// pages/Dashboard.tsx
export function Dashboard() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="space-y-6">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-3xl font-semibold outline-none"
      >
        Dashboard
      </h1>

      <FinancialHealthCard />

      <KpiStrip />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <CategoryBudgetsCard />
        <GoalBudgetsCard />
      </div>
    </div>
  );
}
```

### Page heading

Same focus-on-mount + `outline-none` pattern as `PagePlaceholder`. Screen readers announce "Dashboard" on entry; no visible focus rectangle. The heading sits above the cards, not inside one — it's the page title, not a card title.

### Card chrome convention

Every section uses shadcn `Card` + `CardHeader` + `CardContent`. The header contains the section title (`<h2>` via `CardTitle`) and, where applicable, a small "View all →" link aligned to the right via `flex justify-between`:

```tsx
// CategoryBudgetsCard.tsx (sketch)
<Card>
  <CardHeader className="flex flex-row items-baseline justify-between">
    <CardTitle>Category Budgets</CardTitle>
    <Link to="/budgets/categories" className="text-sm text-muted-foreground hover:text-foreground">
      View all →
    </Link>
  </CardHeader>
  <CardContent>
    <CategoryBudgetBars />
  </CardContent>
</Card>
```

"View all →" appears on Category Budgets, Goal Budgets, and Reminders. It does NOT appear on Financial Health, Net Worth, or MTD — none of those have dedicated detail pages yet.

### No max-width on the Dashboard page

Cards stretch to the full width of `<main>` (which is itself bounded by the viewport minus the sidebar). On a 2560px monitor the cards will be wide; that's acceptable — finance dashboards benefit from horizontal real estate. Individual content inside cards (e.g., the KPI cards' stat displays) can have their own max-width if needed, but the card containers themselves do not.

---

## 3. Component contracts

Each card is self-contained. Rendering states (loading / error / empty / data) are per-card, so the dashboard renders progressively — fast endpoints arrive first.

### Common state machine

Every card moves through:

1. **Loading** — `Skeleton` placeholder shapes that mimic the final content layout. Shown until the first response arrives.
2. **Error** — small inline message inside the card body: `⚠ Couldn't load <section name>.` plus a `Retry` button (calls the `useApi` hook's `refetch`). Other cards keep working.
3. **Empty** — when the endpoint returns success with no data. Each card defines its own empty copy (see below).
4. **Data** — real content rendered.

The `Skeleton` shadcn primitive is added via `pnpm dlx shadcn add skeleton` in the implementation plan.

### `FinancialHealthCard`

- **Endpoint:** `GET /api/dashboard/health`
- **Response DTO:**
  ```ts
  type HealthDto = {
    liquid: number;
    billsDue: number;
    availableToday: number;
    budgetReserved: number;
    safeToSpend: number;
    warningLevel: 'OK' | 'Caution' | 'Critical';
  };
  ```
- **Renders:** a row of stat tiles for the breakdown values (each amount in `<Numeric>` mono), plus a prominent "Safe to Spend" treatment with `WarningLevel` reflected as a colored `Badge`:
  - `OK` → success (emerald) badge
  - `Caution` → warning (amber) badge
  - `Critical` → destructive (rose) badge
- **Empty:** never empty; if the endpoint returns `null`, treat it as an error state (the financial state is always *some* state).

### `KpiStrip`

A thin presentational container that renders the three child cards inside the `grid-cols-1 sm:grid-cols-3` grid. Has no own data-fetching. Tested for composition only (renders all three children in order).

### `NetWorthCard`

- **Endpoint:** `GET /api/dashboard/summary` (Net Worth slice)
- **Response DTO:** consolidated `SummaryDto` — see §4 for full shape. Net Worth slice:
  ```ts
  netWorth: Array<{
    currencyCode: string;
    currencySymbol: string;
    assets: number;
    liabilities: number;
    netWorth: number;
  }>;
  ```
- **Renders:**
  - **Single currency** (one entry in the array): stat display — three labelled rows (Assets / Liabilities / Net Worth) with `<Numeric>` amounts. Net Worth row uses success color when ≥ 0, destructive color when < 0.
  - **Multi-currency** (more than one entry): mini-table with one row per currency, columns Currency / Assets / Liabilities / Net Worth.
- **Empty:** "No accounts yet. Add one to see your net worth." (muted).

### `MtdCard`

- **Endpoint:** `GET /api/dashboard/summary` (MTD slice)
- **Response DTO:**
  ```ts
  mtd: {
    currencyCode: string;
    currencySymbol: string;
    income: number;
    expenses: number;
    savingsRate: number; // fraction, e.g. 0.42 for 42%
  };
  ```
- **Renders:** three stat lines:
  - "Income" — value in `<Numeric>` with success color
  - "Expenses" — value in `<Numeric>` with destructive color
  - "Savings Rate" — value in `<Numeric>`, formatted as a percentage with one decimal (e.g. "42.3%")
- **Empty:** "No transactions this month yet." (muted).

### `RemindersCard`

- **Endpoint:** `GET /api/dashboard/summary` (reminders slice)
- **Response DTO:**
  ```ts
  remindersDueCount: number;
  ```
- **Renders:**
  - When `0`: muted "No reminders due."
  - When `>0`: bold count + "reminder(s) due" + `Link` to `/app/recurring` labelled "View all →"
- **Empty:** the `0` case IS the empty state.

### `CategoryBudgetsCard`

- **Endpoint:** `GET /api/dashboard/category-budgets` (existing)
- **Response DTO:** unchanged — the same shape `CategoryBudgetBars.tsx` already consumes.
- **Renders:** wraps the existing `CategoryBudgetBars` inside our new card chrome:
  - Header: "Category Budgets" + "View all →" link to `/app/budgets/categories`
  - Content: `<CategoryBudgetBars />` (unchanged)
- **Empty:** the existing component renders its own empty state. Wrapper does nothing extra.

### `GoalBudgetsCard`

- Same pattern as `CategoryBudgetsCard`, with `GoalBudgetBars` and the link `/app/budgets/goals`.

### The `useApi` hook

Single new hook at `src/app/lib/use-api.ts`. Used by every card with its own data fetch:

```ts
const { data, error, loading, refetch } = useApi<HealthDto>('/api/dashboard/health');
```

Behavior:
- Fetches on mount.
- Cancels in-flight request on unmount via `AbortController`.
- Returns `{ loading: true, data: undefined, error: undefined }` initially.
- On success: `{ loading: false, data: <parsed JSON>, error: undefined }`.
- On HTTP error or network failure: `{ loading: false, data: undefined, error: Error }`.
- `refetch()` re-runs the request and resets the state machine.

Tested in isolation in `use-api.test.ts`.

---

## 4. Backend audit and `/api/dashboard/summary`

### Audit task (first task in implementation plan)

Read `ProjectCeres/Controllers/Api/DashboardApiController.cs` and `ProjectCeres/Controllers/DashboardController.cs` to confirm:

1. `GET /api/dashboard/health` returns the `HealthDto` shape declared in §3.
2. `GET /api/dashboard/category-budgets` and `GET /api/dashboard/goal-budgets` exist and return data compatible with the existing React island components (which they do — those components already consume them in production).
3. **Net Worth + MTD + Reminders data is currently only computed in `DashboardController.Index()`** for its `DashboardData` ViewModel. There is no API endpoint exposing it.

If any of (1) or (2) is not as expected, the implementation plan flags it and adapts before continuing.

### New endpoint: `GET /api/dashboard/summary`

Bundles the three small KPIs into a single response (one round-trip; they're all small).

**Response shape:**

```json
{
  "netWorth": [
    {
      "currencyCode": "EUR",
      "currencySymbol": "€",
      "assets": 14500.00,
      "liabilities": 2300.00,
      "netWorth": 12200.00
    }
  ],
  "mtd": {
    "currencyCode": "EUR",
    "currencySymbol": "€",
    "income": 3200.00,
    "expenses": 1850.45,
    "savingsRate": 0.4217
  },
  "remindersDueCount": 2
}
```

**Implementation approach:**

- Add `[HttpGet("summary")]` action to `DashboardApiController`.
- The action calls into the existing `DashboardService` (or wherever `DashboardController.Index` builds its ViewModel) and shapes the response as JSON.
- Reuses existing service logic — no new service methods unless `DashboardService` doesn't expose the constituent pieces independently.
- `savingsRate` is returned as a fraction (0.4217), not a percentage (42.17). The React side formats for display.
- `remindersDueCount` uses the existing `RecurringTransactionService.GetUpcomingAsync(withinDays: N)` count call (the Razor view already does this).

**Add the route to `docs/api-contract.md`** in the Phase 3 endpoints table:

| Endpoint | Method | Path | Description |
|---|---|---|---|
| Dashboard summary | GET | `/api/dashboard/summary` | Consolidated KPIs: net worth (per currency), MTD income/expenses/savings rate, pending reminders count |

### Auth note

No auth in Phase 3 yet. This endpoint is open like all other API endpoints. When auth lands (`[Authorize]` global fallback policy in `planning-phase3.md`), this endpoint will require an authenticated session like everything else. No special handling needed in this plan.

---

## 5. Testing

### React tests

- `FinancialHealthCard.test.tsx` — three cases: loading shows skeleton; error shows retry button; success renders the spendable breakdown with the WarningLevel badge using the right semantic color.
- `KpiStrip.test.tsx` — composition: renders all three child cards in order. Children are mocked to keep the test focused on the strip itself.
- `NetWorthCard.test.tsx` — loading/error/empty/data; single-currency renders stat display, multi-currency renders mini-table; amounts use `<Numeric>`; net worth color flips on negative values.
- `MtdCard.test.tsx` — loading/error/empty/data; income uses success color, expenses use destructive color, savings rate formats as a percentage with one decimal.
- `RemindersCard.test.tsx` — when count is 0, renders muted "No reminders due" with no link; when count is >0, renders the count and "View all →" link to `/app/recurring`.
- `CategoryBudgetsCard.test.tsx` — wraps existing `CategoryBudgetBars`; the wrapper renders the card header, the "View all →" link, and forwards data to the inner component. Inner component's own behaviour isn't re-tested.
- `GoalBudgetsCard.test.tsx` — same pattern.
- `Dashboard.test.tsx` — page-level smoke: renders the heading "Dashboard" as `<h1>`; mounts all four sections (verified by the presence of each card's title); the page doesn't crash when API endpoints are mocked to return mixed data and errors.
- `use-api.test.ts` — the hook in isolation: returns loading initially, transitions to data on success, transitions to error on fetch reject, `refetch` re-runs the request, in-flight request is cancelled on unmount.

### Backend integration test

- `DashboardApiSummaryTests.cs` (xUnit + FluentAssertions, hits real test DB):
  - Returns 200 with the documented shape.
  - The response includes a `netWorth` array, an `mtd` object, and `remindersDueCount`.
  - Multi-currency users return multiple `netWorth` entries.
  - `savingsRate` is a fraction (0–1), not a percentage (0–100).
  - When the user has no transactions in the current month, `mtd.income` and `mtd.expenses` are both 0 and `savingsRate` is 0.

### Mocking strategy

- React tests mock `fetch` via `vi.spyOn(global, 'fetch')` to return controlled responses. We do NOT mock `useApi` — we mock the layer below it, so the hook itself is exercised in every component test.
- Backend tests use the existing test fixture (real Postgres test DB) — no mocking of services.

### Not tested

- Visual layout (which breakpoint hits when) — browser-viewport thing, not jsdom.
- The existing `CategoryBudgetBars` and `GoalBudgetBars` components — already tested in their existing test files.
- The DTO serialization on the .NET side beyond what the integration test asserts.

### Manual verification at end of plan

- Visit `https://localhost:<port>/app/` — all four sections render with real data from the local DB.
- Forcibly break one endpoint (e.g., temporarily throw in the controller) — that one card shows the error state, others keep working.
- Verify the layout responds correctly at 320px, 800px, 1024px, 1920px viewports — KPI strip flips at 640px, Budgets row flips at 1024px.
- Verify the existing Razor `/Dashboard` page still works untouched — no regression in the parallel app.

---

## 6. Out of scope

Belong to follow-up plans, not this one:

- **The remaining six Razor Dashboard sections** — Net Worth chart, Income/Expense chart, Spending by Category chart, Account Balances chart, Cash Flow chart, and any other widgets present in the existing `Index.cshtml`. Land in the next Dashboard plan.
- **The 302 redirect from `/Dashboard` → `/app/`** and the deletion of `Index.cshtml` + `_HealthSnapshot.cshtml` + unused `DashboardData` ViewModel. Both happen in the next Dashboard plan, after all sections are ported. Pattern documented in [`planning-phase3-spa-migration.md`](../../planning-phase3-spa-migration.md#per-area-redirect-rules-during-migration).
- **TanStack Query** — `useApi` hook is sufficient until adoption criteria in [`planning-future.md`](../../planning-future.md#tanstack-query-server-data-caching-layer) are met.
- **Auth** — no `[Authorize]` policy or session checking. Phase 3 auth lands in its own plan.
- **Health card "View all →"** — no destination yet. Add later when a Spendable Balance breakdown detail page exists.
- **Dashboard customisation** (drag-to-reorder cards, hide/show widgets) — explicitly not in scope; the layout is fixed.
- **Real-time updates** (websocket / polling) — not in scope; data fetched once on mount, refetch via the per-card Retry button.
- **Localized number formatting** — uses the existing `Numeric` component which renders raw values; full Intl.NumberFormat support arrives with the localization plan.
