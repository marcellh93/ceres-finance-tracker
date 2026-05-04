# Data-loading ease-in — design spec

**Date:** 2026-05-04
**Status:** Approved for planning
**Scope:** SPA client (`ProjectCeres.Client/`)

## Problem

Skeletons appear and disappear too abruptly. The current `useApi` hook flips `loading` to `true` synchronously on mount or URL change, which renders the skeleton on the very next frame. When the response returns quickly the skeleton flashes for a few frames and then snaps to real data — a visible "pop" with no easing.

Page navigation itself is fine and must remain instant. The friction is inside the page, at the moment data arrives.

## Goal

Make the data-arrival sequence feel calm and automatic:

- Fast responses (under ~150 ms) should never show a skeleton at all.
- Slow responses should fade the skeleton in, then cross-fade to data when it arrives.
- The transition must respect `prefers-reduced-motion`.
- No layout shift during the cross-fade.

Page-to-page navigation is unchanged — only the data area inside a page is softened.

## Non-goals

- Changing the existing `useApi` hook's data-fetching behaviour.
- Introducing a global loading store, request deduplication, or TanStack Query (still deferred per `docs/planning-future.md`).
- Redesigning the visual skeleton itself (the `animate-pulse` style stays as-is).
- Rolling the new pattern out to every page in this spec — only Accounts is covered here. Other pages are a separate follow-up.

## Design

Two composable pieces, plus one reference implementation on the Accounts page.

### 1. `useDelayedLoading` hook

**Location:** `src/app/lib/use-delayed-loading.ts`

**Signature:**

```ts
export function useDelayedLoading(
  loading: boolean,
  options?: { delay?: number },
): boolean;
```

**Behaviour:**

- Returns `showSkeleton: boolean`.
- When `loading` flips from `false` → `true`, starts a timer of `delay` ms (default `150`). `showSkeleton` stays `false` until the timer fires.
- When `loading` flips from `true` → `false`, cancels any pending timer and immediately sets `showSkeleton` to `false`.
- If `loading` returns to `true` after a previous cycle, the delay restarts.

**Why 150 ms:** below this threshold, humans don't perceive a delay as "waiting." Responses faster than this skip the skeleton entirely; responses slower than this get a calm reveal.

**Tests** (`use-delayed-loading.test.ts`, Vitest with fake timers):

- Fast cycle (`true` → `false` within 100 ms) never returns `true`.
- Slow cycle (`true` held for 200 ms) returns `true` after 150 ms.
- Custom `delay` option respected.
- Unmount during pending timer does not throw.

### 2. `<DataTransition>` component

**Location:** `src/app/components/DataTransition.tsx`

**Props:**

```ts
type State = 'skeleton' | 'data' | 'error';

interface DataTransitionProps {
  state: State;
  skeleton: React.ReactNode;
  error: React.ReactNode;
  children: React.ReactNode; // the data view
}
```

**Behaviour:**

- Cross-fades between the three states using `opacity` transitions tied to design-system tokens: `transitionDuration: var(--motion-duration-base)` (180 ms), `transitionTimingFunction: var(--motion-easing-standard)`.
- During a transition, both the outgoing and incoming child render simultaneously (absolute positioning inside a `relative` container) so the cross-fade is visual rather than a height pop.
- The container's height is driven by the currently-visible child via `min-height` from the most recently rendered content — preventing layout shift when skeleton and data have similar but not identical heights.
- Respects `prefers-reduced-motion: reduce` — when matched, transitions collapse to an instant swap with no opacity animation. Combined with `useDelayedLoading`, fast responses still skip the skeleton, so reduced-motion users still avoid the flash.

**Why this shape:** keeping all three states (skeleton / data / error) in one component means consumers don't write conditional render chains; the transition logic lives in one place.

**Tests** (`DataTransition.test.tsx`):

- Renders only the child for the current state (after transition settles).
- Switching states applies the opacity transition class/style.
- `prefers-reduced-motion` mock disables the transition.

### 3. Reference rollout — Accounts page

**File:** `src/app/features/accounts/AccountsLayout.tsx`

Two surfaces change:

**(a) Currency subtotals card** — currently at line 74:

```tsx
{list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}
```

Replace with a `<DataTransition>` wrapped around `<AccountCurrencySubtotals>`, driven by `useDelayedLoading(list.loading)`. Skeleton: a small card placeholder matching the subtotals card height.

**(b) Accounts list body** — currently the `AccountsBody` function at lines 113–177:

Replace the manual `if (list.loading && !list.data) return <Skeleton... />` / `if (list.error) return <CardError />` / fall-through-to-table chain with a single `<DataTransition>` driven by `useDelayedLoading(list.loading)`.

Three states map cleanly:

- `skeleton` — the existing 4-row skeleton block (preserves `data-testid="accounts-skeleton"` for existing tests)
- `error` — the existing `<CardError section="Accounts" onRetry={list.refetch} />`
- `data` — the existing empty-state branches OR `<AccountsTable rows={sorted} ... />` (both treated as "data has arrived")

State derivation:

```ts
const showSkeleton = useDelayedLoading(list.loading && !list.data);
const state: State =
  showSkeleton ? 'skeleton'
  : list.error  ? 'error'
  : 'data';
```

The `&& !list.data` guard preserves the existing behaviour where a refetch with stale data shows the stale data, not a skeleton.

**Tests** (in `AccountsLayout.test.tsx`):

- *Fast response skips skeleton.* Mock `fetch` to resolve immediately; assert `accounts-skeleton` is never queried (use `queryByTestId` and assert `null` after `findByRole('table')` resolves).
- *Slow response shows skeleton then data.* Use `vi.useFakeTimers()`; hold the fetch promise; advance 200 ms; assert skeleton visible; resolve fetch; assert table visible.
- *Reduced-motion path.* Mock `window.matchMedia('(prefers-reduced-motion: reduce)')` → matches; assert the `<DataTransition>` root carries the no-transition marker (data attribute or class — TBD by implementation).

### Browser verification (post-implementation)

Per `feedback_ui_work_checklist` and `docs/design-system.md` § Working rules:

- Start `dotnet run --project ProjectCeres` and `pnpm dev`.
- Open `/accounts` with no network throttling — confirm no skeleton flash, table fades in.
- Open `/accounts` with DevTools "Slow 3G" throttling — confirm skeleton fades in, then cross-fades to table.
- Toggle "Include archived" switch to trigger a refetch — confirm stale rows stay visible (no skeleton flash because of the `&& !list.data` guard).
- Type in the filter input — confirm no skeleton flash on each keystroke (debounced search, but worth checking).
- Force the fetch to fail (block the URL in DevTools) — confirm error card cross-fades in.
- macOS System Preferences → Reduce Motion ON — repeat fast and slow paths; confirm no animation but also no skeleton flash on fast loads.

## Out of scope (separate follow-up)

Rolling `<DataTransition>` + `useDelayedLoading` out to:

- Categories, Movements, Recurring, Reports, Budgets, Settings, Import pages
- Dashboard cards (NetWorth, MTD, IncomeExpense, NetWorthChart, CashFlow, AccountBalances, SpendingByCategory, FinancialHealth, Reminders)

Each of these will follow the Accounts pattern but is planned and shipped separately so we can sanity-check the feel in the browser before a wide diff.

## Open questions

None — all clarifying questions resolved during brainstorming.
