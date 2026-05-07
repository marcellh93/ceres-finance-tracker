# Data-loading ease-in — full rollout plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the `useDelayedLoading` + `<DataTransition>` pattern (proven on Accounts) to every remaining data-loading surface in the SPA so the whole app feels equally calm on data arrival.

**Architecture:** Mechanical rollout. The primitives already exist:
- `useDelayedLoading(loading, options?)` — `ProjectCeres.Client/src/app/lib/use-delayed-loading.ts`
- `<DataTransition>` (with `DataTransitionState`) — `ProjectCeres.Client/src/app/components/DataTransition.tsx`

Each task wraps an existing skeleton/error/data branching block in `<DataTransition>` driven by `useDelayedLoading(loading && !data)`. State derivation is uniform across all consumers:
- `skeleton` — delay elapsed AND no stale data
- `error` — error AND no stale data
- `data` — otherwise (includes empty-state branches)

**The "share fetch → share transition" rule.** When a page has multiple visible sections that all read from a single fetch (e.g., a totals/header card AND a list, both projected from the same array), they MUST share one DataTransition wrapper at the page level. The original Accounts implementation used per-section DataTransitions — different skeleton heights diverged from real-content heights, so when data arrived the list reflowed by more than the totals card, making the totals card appear to "arrive late." Refactored to page-level in commit `4e49f25`. **The canonical reference is now `AccountsLayout.tsx` after that commit, not before.**

Decision tree per page (verified against current code, applied below):

| Shape | Pattern |
|---|---|
| Single fetch → single visible section | Section-level wrapper (filter row stays live; only the table area transitions) |
| Single fetch → multiple visible sections (totals + list, header card + body) | **Page-level wrapper.** Skeleton mirrors the full layout (totals-skeleton + list-skeleton). Filter row rendered inside both skeleton and data slots so it's live during loading. |
| Multiple parallel fetches feeding one combined view (e.g., form data + dropdown options) | **Page-level wrapper** keyed off combined `loading = a.loading || b.loading`. The form is the data slot. |
| Multiple fetches feeding alternative tabs (only one shown at a time) | **Per-tab wrapper.** Each tab has its own DataTransition; they're not parallel sections, they're alternatives. |
| Independent KPI tiles on a dashboard | **Per-card wrapper.** "Filling in" is the expected dashboard pattern; lockstep would actually feel slower. |

**Tech Stack:** React 19, TypeScript, Tailwind CSS v4, Vitest + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-05-04-data-loading-ease-in-design.md` (Accounts primitives reference at `docs/superpowers/plans/2026-05-04-data-loading-ease-in.md`)

**Reference implementation:** post-refactor `AccountsLayout.tsx` (commit `4e49f25`). Read it before any multi-section page below — it's the canonical page-level shape.

---

## Rollout order and rationale

Order is **list pages first, then dashboard cards, then settings/review**. List pages share the most complex shape (skeleton + error + empty-state + table); getting them right validates the pattern across variants. Dashboard cards are individually simpler but numerous. Settings is a single page; Review is a small list.

**Test discipline:** After each task, run the per-page test suite (`pnpm --dir ProjectCeres.Client test -- <PageName>`) to confirm no regressions. The synchronous-skeleton tests (e.g., `it('renders skeleton while loading', () => ...)`) MUST be rewritten to async with `waitFor` + `{ timeout: 500 }` exactly like Accounts (see `AccountsLayout.test.tsx:48-56`). This is the single most common failure mode of this rollout — every list page has at least one such test.

**Per-task commit boundary:** Stage only the explicitly named files. The user's working tree often contains parallel work; `git add .` is forbidden.

---

## File Structure

No new files in this plan — every task modifies existing pages.

**Per-task imports to add (always the same two lines, in alphabetical position within the existing imports block):**

```tsx
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
```

(Adjust `../../` to match the consumer's depth. From `features/<x>/` it's `../../components/...`. From `features/dashboard/` it's also `../../components/...`. From `layout/` it's `./DataTransition` and `../lib/use-delayed-loading` if applicable — verify per file.)

---

## Task 1: Categories list page

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/categories/CategoriesLayout.test.tsx`

- [ ] **Step 1: Read current state**

Open `CategoriesLayout.tsx`. Locate the `useApi` hook usage and the conditional skeleton/error/data branches. Identify the `data-testid="...-skeleton"` testid (likely `categories-skeleton`) — note the exact value, you'll preserve it.

- [ ] **Step 2: Add imports**

Add the two standard imports (see "Per-task imports" above) to the existing imports block.

- [ ] **Step 3: Wrap the list body**

Mirror `AccountsLayout.tsx:113-183` (`AccountsBody`). Pull the skeleton block, error block, and data render into the three `<DataTransition>` slots. State derivation:

```tsx
const showSkeleton = useDelayedLoading(list.loading && !list.data);
let state: DataTransitionState;
if (showSkeleton && !list.data) state = 'skeleton';
else if (list.error && !list.data) state = 'error';
else state = 'data';
```

If the empty-state and table-render branches are intertwined, extract them into a `CategoriesDataView` sibling component (mirror `AccountsLayout.tsx:185-228`).

- [ ] **Step 4: Run the existing test suite**

Run: `pnpm --dir ProjectCeres.Client test -- CategoriesLayout`
Expected: most tests pass; the synchronous-skeleton test (if any) FAILS because the skeleton no longer appears synchronously.

- [ ] **Step 5: Rewrite the synchronous-skeleton test**

Find the test that reads roughly:
```tsx
it('renders skeleton while loading', () => {
  mockFetch.mockImplementation(() => new Promise(() => {}));
  renderAt('/categories');
  expect(screen.getByTestId('categories-skeleton')).toBeInTheDocument();
});
```

Replace with:
```tsx
it('renders skeleton after the delay window when loading is slow', async () => {
  mockFetch.mockImplementation(() => new Promise(() => {}));
  renderAt('/categories');
  expect(screen.queryByTestId('categories-skeleton')).toBeNull();
  await waitFor(
    () => expect(screen.getByTestId('categories-skeleton')).toBeInTheDocument(),
    { timeout: 500 },
  );
});
```

Substitute the actual route path and testid as observed in Step 1.

- [ ] **Step 6: Re-run tests**

Run: `pnpm --dir ProjectCeres.Client test -- CategoriesLayout`
Expected: all tests green.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx ProjectCeres.Client/src/app/features/categories/CategoriesLayout.test.tsx
git diff --cached --stat   # verify only these two files
git commit -m "feat(categories): cross-fade list body skeleton/error/data states"
```

---

## Task 2: Movements list page

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx` (if it exists — verify with `ls`)

Same recipe as Task 1 with `movements`-prefixed identifiers.

- [ ] **Step 1: Read current state.** Note the testid value and any custom skeleton block (Movements may have a multi-row skeleton).

- [ ] **Step 2: Add the two standard imports.**

- [ ] **Step 3: Wrap the list body.** Use the same state-derivation block as Task 1. If filter UI sits above the skeleton (date pickers, account/category pickers), wrap only the table area in `<DataTransition>` — leave filters always visible. Match the Accounts pattern: filters outside, `<DataTransition>` around the body.

- [ ] **Step 4: Run tests.** `pnpm --dir ProjectCeres.Client test -- MovementsLayout`

- [ ] **Step 5: Rewrite the synchronous-skeleton test** if present (same pattern as Task 1).

- [ ] **Step 6: Re-run tests.**

- [ ] **Step 7: Commit.**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx ProjectCeres.Client/src/app/features/movements/MovementsLayout.test.tsx
git diff --cached --stat
git commit -m "feat(movements): cross-fade list body skeleton/error/data states"
```

---

## Task 3: Recurring list page (page-level pattern)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx`

**Pattern:** RecurringLayout has two parallel `useApi` calls (`list` + `allList` for empty-state determination), structurally identical to the post-refactor Accounts page. Use the **page-level wrapper** — single DataTransition keyed off `list.loading && !list.data`. See `AccountsLayout.tsx` (commit `4e49f25`) for the exact shape.

- [ ] **Step 1: Read current state.** Note the skeleton testid, any per-row layout choices, and where the filter row + "include archived" Switch live in the JSX.

- [ ] **Step 2: Add the two standard imports** plus `useRef`/`type ReactNode` if needed.

- [ ] **Step 3: Hoist `useDelayedLoading` to the top of the component**, BEFORE the `if (childActive)` early return. (React Rules of Hooks — see Accounts commit for the precedent.)

- [ ] **Step 4: Extract the filter row into a `filterRow: ReactNode` const** (Input + Switch + New button). Render it inside both the skeleton slot and the data slot so it stays live during loading.

- [ ] **Step 5: Build the skeleton slot** — a `<div className="space-y-6">` containing any header-card-skeleton (if Recurring has a totals strip) plus the filter card with the existing list-skeleton. Preserve `data-testid="recurring-skeleton"` on the row-skeleton block.

- [ ] **Step 6: Build the data slot** as a sibling component (e.g., `RecurringDataLayout`) that takes `list`, `allList`, `query`, `filterRow` as props. Mirror `AccountsDataLayout`.

- [ ] **Step 7: Run tests.** `pnpm --dir ProjectCeres.Client test -- RecurringLayout`. Rewrite the synchronous-skeleton test to async (per Task 1 template).

- [ ] **Step 8: Commit.**

```
feat(recurring): atomic page-level data transition
```

---

## Task 4: Reports — list page (ReportsLayout) and per-report views

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx` (if it has its own skeleton — verify)
- Modify each per-report file that uses `useApi` + a Skeleton block:
  - `BudgetVsActual.tsx`
  - `ExpenseBreakdown.tsx`
  - `IncomeExpense.tsx`
  - `LargestExpenses.tsx`
  - `MonthlyCashFlow.tsx`
  - `NetWorth.tsx`
  - `NetWorthOverTime.tsx`
  - `TransactionHistory.tsx`
- Modify each report's `.test.tsx` if it has a synchronous-skeleton test

⚠️ **Pre-check:** The user's working tree has changes in `ReportHeader.tsx` and `reports-api.ts`. Same rule as Task 3 — STOP and report if those files have parallel work, since the reports area is actively being edited.

This task is bigger because Reports is 8 files. **Split into 8 sub-commits**, one per report, so each is small enough to review and revert independently if needed.

For each report file:

- [ ] **Substep A: Add the two standard imports.**

- [ ] **Substep B: Wrap the chart/table body.** Reports have a common shape: `<ReportHeader />` and `<ReportLocalFilterBar />` at the top (always visible), then a chart/table area. Wrap only the chart/table area in `<DataTransition>`. The skeleton slot should match the chart's rendered height (`h-[220px]` per `docs/design-system.md` § Skeletons line 158).

- [ ] **Substep C: Rewrite synchronous-skeleton test if present.**

- [ ] **Substep D: Run that report's test suite.** `pnpm --dir ProjectCeres.Client test -- <ReportFilename>`

- [ ] **Substep E: Commit.** One commit per report:

```bash
git add ProjectCeres.Client/src/app/features/reports/<Report>.tsx [optional: <Report>.test.tsx]
git diff --cached --stat
git commit -m "feat(reports): cross-fade <report-kebab-case> on data load"
```

Examples:
- `feat(reports): cross-fade budget-vs-actual on data load`
- `feat(reports): cross-fade expense-breakdown on data load`
- ... (8 commits total)

⚠️ **Known flaky test:** `MonthlyCashFlow.test.tsx` has a pre-existing flake (Recharts measurement span colliding with `getByText`). If it fails on your run AND the failure is the Recharts one (not your changes), document the flake in the task report; do not block the commit.

---

## Task 5: Budgets list page (per-tab pattern)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.test.tsx` (if exists)

**Pattern:** BudgetsLayout has two parallel `useApi` calls (`categoryQuery`, `goalQuery`) that power **two tabs** (Category, Goal). Only one tab is rendered at a time. They are NOT parallel sections — they're alternative views. Use the **per-tab pattern**: each tab gets its own DataTransition keyed off its own query. The other tab's loading state is irrelevant when the user is on this tab.

- [ ] **Step 1: Read current state.** Note where `categoryQuery.loading` and `goalQuery.loading` are checked, which Skeleton renders for each, and how the active tab is selected.

- [ ] **Step 2: Add the two standard imports.**

- [ ] **Step 3: Wrap each tab's body** in its own `<DataTransition>`. State derivation is identical to Task 1's cheat sheet, but applied independently per tab.

- [ ] **Step 4: Run tests.** `pnpm --dir ProjectCeres.Client test -- BudgetsLayout`. Rewrite synchronous-skeleton tests if present.

- [ ] **Step 5: Commit.**

```
feat(budgets): cross-fade per-tab body on data load
```

---

## Task 6: Settings page (page-level, combined loading)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx`
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx`

**Pattern:** SettingsPage has two parallel `useApi` calls (`settings` for the form values, `currencies` for a dropdown). The form can't render meaningfully until both are loaded. Use the **page-level wrapper** keyed off a combined boolean: `loading = settings.loading || currencies.loading`, with `hasData = settings.data && currencies.data`.

- [ ] **Step 1: Read current state.** Confirm the form is gated behind both `settings.loading` and `currencies.loading` and a Skeleton renders during load.

- [ ] **Step 2: Add the two standard imports.**

- [ ] **Step 3: Wrap the form area in `<DataTransition>`** with combined loading:

```tsx
const loading = settings.loading || currencies.loading;
const hasData = !!settings.data && !!currencies.data;
const showSkeleton = useDelayedLoading(loading && !hasData);
let state: DataTransitionState;
if (showSkeleton && !hasData) state = 'skeleton';
else if ((settings.error || currencies.error) && !hasData) state = 'error';
else state = 'data';
```

- [ ] **Step 4: Run tests.** `pnpm --dir ProjectCeres.Client test -- SettingsPage`. Rewrite synchronous-skeleton test if present.

- [ ] **Step 5: Commit.**

```
feat(settings): cross-fade settings form on data load
```

---

## Task 7: Review (ReconciliationList)

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/review/ReconciliationList.tsx`
- Modify: `ProjectCeres.Client/src/app/features/review/ReconciliationList.test.tsx` (if exists)

Same recipe as Task 1.

- [ ] **Steps 1–7: Same as Task 1.** Commit message:

```
feat(review): cross-fade reconciliation list on data load
```

---

## Task 8: Dashboard cards (9 cards, one commit per card)

**Files:** each card uses the simpler shape — the whole card body is the `<DataTransition>` target. Filters live elsewhere. Skeleton block sizes match the rendered card per `docs/design-system.md` § Skeletons line 158 (`h-[220px]` for chart cards, `h-5 w-32` for KPI text rows).

For each card below, follow the **dashboard card recipe**:

**Dashboard card recipe (per file):**

- [ ] **A. Read current state.** Identify the `useApi` hook, the loading branch (Skeleton), the error branch, the data branch.
- [ ] **B. Add the two standard imports** (`../../components/DataTransition`, `../../lib/use-delayed-loading`).
- [ ] **C. Wrap the card body in `<DataTransition>`** with the three slots. Skeleton slot reuses the existing `<Skeleton>` block. Error slot reuses the existing error branch (`<CardError section="..." onRetry={refetch} />` if present, else build it with `CardError`).
- [ ] **D. Run tests.** `pnpm --dir ProjectCeres.Client test -- <CardName>`
- [ ] **E. Rewrite synchronous-skeleton test if present.**
- [ ] **F. Re-run tests.**
- [ ] **G. Commit.** One commit per card.

**Cards to roll out (9 total):**

- [ ] **8.1. NetWorthCard** — `dashboard/NetWorthCard.tsx`. Commit: `feat(dashboard): cross-fade NetWorthCard on data load`
- [ ] **8.2. MtdCard** — `dashboard/MtdCard.tsx`. Commit: `feat(dashboard): cross-fade MtdCard on data load`
- [ ] **8.3. FinancialHealthCard** — `dashboard/FinancialHealthCard.tsx`. Commit: `feat(dashboard): cross-fade FinancialHealthCard on data load`
- [ ] **8.4. RemindersCard** — `dashboard/RemindersCard.tsx`. Commit: `feat(dashboard): cross-fade RemindersCard on data load`
- [ ] **8.5. NetWorthChart** — `dashboard/NetWorthChart.tsx`. Commit: `feat(dashboard): cross-fade NetWorthChart on data load`
- [ ] **8.6. CashFlowChart** — `dashboard/CashFlowChart.tsx`. Commit: `feat(dashboard): cross-fade CashFlowChart on data load`
- [ ] **8.7. IncomeExpenseChart** — `dashboard/IncomeExpenseChart.tsx`. Commit: `feat(dashboard): cross-fade IncomeExpenseChart on data load`
- [ ] **8.8. AccountBalancesChart** — `dashboard/AccountBalancesChart.tsx`. Commit: `feat(dashboard): cross-fade AccountBalancesChart on data load`
- [ ] **8.9. SpendingByCategoryChart** — `dashboard/SpendingByCategoryChart.tsx`. Commit: `feat(dashboard): cross-fade SpendingByCategoryChart on data load`

---

## Task 9: Final verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full client test suite.** `pnpm --dir ProjectCeres.Client test`
Expected: all tests green. The pre-existing `MonthlyCashFlow.test.tsx` Recharts flake may still be present — document but do not block.

- [ ] **Step 2: Build the client.** `pnpm --dir ProjectCeres.Client build`
Expected: type-check + production build succeeds.

- [ ] **Step 3: Browser sweep.** Start `dotnet run --project ProjectCeres` and `pnpm --dir ProjectCeres.Client dev`. Open each rolled-out page (`/categories`, `/movements`, `/recurring`, each report URL, `/budgets`, `/settings`, `/review`, `/`) and visually verify:
  - Fast path: no skeleton flash, smooth fade-in
  - Slow path (DevTools custom 500/500/400 throttling): skeleton fades in, cross-fades to data
  - Reduced motion (DevTools Cmd+Shift+P → "Emulate CSS prefers-reduced-motion: reduce"): instant swap, no animation
- [ ] **Step 4: Commit nothing — report findings.** If any page looks wrong, file the specific issue (page + path + observation). If all pages look right, the rollout is complete.

---

## Out of scope

- Form pages that are NOT primary data loaders (e.g., Account/Category/Recurring/Budget create/edit forms). They share the page-load behaviour of their list parent and don't need their own transition. Movement edit/create forms similarly inherit from MovementsLayout.
- TopBar, ReminderCountProvider, ReviewCountProvider — these are layout-level data loaders that render small badges. The flicker cost is low and adding transitions to them risks visible "loading badge" patterns.
- AccountLedger — a child route of Accounts that renders inside the Accounts outlet. Defer; it has its own pattern that may need a custom design.
- Any page or component that loads instantly from local state (no `useApi`) — no transition needed.

---

## Pattern reference (cheat sheet)

### Single-fetch, single-section (Tasks 1, 2, 4, 7)

After adding the two imports, the consumer follows this shape:

```tsx
function ConsumerArea({ list }: { list: UseApiResult<...> }) {
  const showSkeleton = useDelayedLoading(list.loading && !list.data);

  let state: DataTransitionState;
  if (showSkeleton && !list.data) state = 'skeleton';
  else if (list.error && !list.data) state = 'error';
  else state = 'data';

  const skeleton = (
    <div data-testid="<page>-skeleton" className="...">
      <Skeleton className="..." />
      {/* match existing skeleton */}
    </div>
  );

  const errorSlot = <CardError section="<Page>" onRetry={list.refetch} />;

  return (
    <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      {/* existing data render: empty-state, table, etc. */}
    </DataTransition>
  );
}
```

### Single-fetch, multiple-section (Tasks 3, plus future page-level adoptions)

For pages where one fetch feeds two visually-parallel sections (totals card + list, header card + body), lift the wrapper to the page level. Mirror `AccountsLayout.tsx` (commit `4e49f25`):

```tsx
export function PageLayout() {
  // Hooks at the top, BEFORE any early return.
  const list = useApi<...>(url);
  const allList = useApi<...>(otherUrl);
  const showSkeleton = useDelayedLoading(list.loading && !list.data);

  if (childActive) return <Outlet ... />;

  let state: DataTransitionState;
  if (showSkeleton && !list.data) state = 'skeleton';
  else if (list.error && !list.data) state = 'error';
  else state = 'data';

  // Filter row rendered in BOTH slots so it's live during loading.
  const filterRow: ReactNode = <>...</>;

  const skeleton = (
    <div className="space-y-6">
      <Card><CardContent className="py-3"><Skeleton className="h-5 w-48" /></CardContent></Card>
      <Card>
        <CardContent className="space-y-4">
          {filterRow}
          <div data-testid="<page>-skeleton" className="space-y-2 py-2">
            {/* row skeletons */}
          </div>
        </CardContent>
      </Card>
    </div>
  );

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>...</header>
      <DataTransition state={state} skeleton={skeleton} error={<CardError ... />}>
        <PageDataLayout list={list} allList={allList} filterRow={filterRow} ... />
      </DataTransition>
    </div>
  );
}
```

### Combined loading from N parallel fetches (Task 6)

When the page can't render until N fetches all settle:

```tsx
const a = useApi<...>(...);
const b = useApi<...>(...);
const loading = a.loading || b.loading;
const hasData = !!a.data && !!b.data;
const showSkeleton = useDelayedLoading(loading && !hasData);
// state derivation continues as above, swapping list.data for hasData.
```

Synchronous-skeleton test rewrite template:

```tsx
it('renders skeleton after the delay window when loading is slow', async () => {
  mockFetch.mockImplementation(() => new Promise(() => {}));
  renderAt('<route>');
  expect(screen.queryByTestId('<page>-skeleton')).toBeNull();
  await waitFor(
    () => expect(screen.getByTestId('<page>-skeleton')).toBeInTheDocument(),
    { timeout: 500 },
  );
});
```
