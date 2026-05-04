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

**Tech Stack:** React 19, TypeScript, Tailwind CSS v4, Vitest + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-05-04-data-loading-ease-in-design.md` (Accounts reference shipped in `docs/superpowers/plans/2026-05-04-data-loading-ease-in.md`)

**Reference implementation:** Accounts at commits `c6052d0` → `5b7d72e`. Read `AccountsLayout.tsx` lines 115–228 (`SubtotalsArea`, `AccountsBody`, `AccountsDataView`) before each task — they show the canonical shape.

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

## Task 3: Recurring list page

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx`

⚠️ **Pre-check:** The user's working tree currently has uncommitted changes in `RecurringLayout.tsx` and `RecurringLayout.test.tsx` (parallel work). Before starting:

```bash
git status -- ProjectCeres.Client/src/app/features/recurring/
```

If those files are modified, **STOP and report** to the user — do not start this task until the user has either committed or stashed their parallel work. The user's `feedback_ask_before_deviating_from_docs` rule requires surfacing this conflict.

- [ ] **Step 1: Read current state.** Note skeleton testid and any per-row layout choices.

- [ ] **Steps 2–7: Same recipe as Task 1.** Commit message:

```
feat(recurring): cross-fade list body skeleton/error/data states
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

## Task 5: Budgets list page

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx`
- Modify: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.test.tsx` (if exists)

Same recipe as Task 1.

- [ ] **Steps 1–7: Same as Task 1.** Commit message:

```
feat(budgets): cross-fade list body skeleton/error/data states
```

---

## Task 6: Settings page

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx`
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx`

Settings is a form page that loads existing settings via `useApi`, then renders a form. The skeleton appears while the initial load resolves. Apply the same wrapper.

- [ ] **Step 1: Read current state.** Confirm the form is gated behind a `loading` check and a Skeleton renders during load.

- [ ] **Step 2: Add the two standard imports.**

- [ ] **Step 3: Wrap the form area in `<DataTransition>`.** State derivation is the same. The `data` slot is the form itself.

- [ ] **Step 4: Run tests.** `pnpm --dir ProjectCeres.Client test -- SettingsPage`

- [ ] **Step 5: Rewrite synchronous-skeleton test if present.**

- [ ] **Step 6: Re-run tests.**

- [ ] **Step 7: Commit.**

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

After adding the two imports, every consumer follows this shape:

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
