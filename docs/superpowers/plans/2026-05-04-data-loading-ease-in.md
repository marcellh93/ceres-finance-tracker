# Data-loading ease-in Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make data-arrival on the Accounts page feel calm — fast responses skip the skeleton entirely, slow responses cross-fade skeleton → data using design-system motion tokens.

**Architecture:** Two new building blocks — a `useDelayedLoading` hook (suppresses skeleton for the first 150 ms after `loading` flips true) and a `<DataTransition>` component (cross-fades between `skeleton` / `error` / `data` slots using `--motion-duration-base` and `--motion-easing-standard`). Apply both to `AccountsLayout` as the reference rollout. Page navigation itself is unchanged — only the data area inside the page is softened. Respects `prefers-reduced-motion`.

**Tech Stack:** React 19, TypeScript, Tailwind CSS v4, Vitest + React Testing Library. Existing `useMediaQuery` hook (`src/app/lib/use-media-query.ts`) handles `prefers-reduced-motion` detection.

**Spec:** `docs/superpowers/specs/2026-05-04-data-loading-ease-in-design.md`

---

## File Structure

**Create:**
- `ProjectCeres.Client/src/app/lib/use-delayed-loading.ts` — the hook
- `ProjectCeres.Client/src/app/lib/use-delayed-loading.test.ts` — hook tests
- `ProjectCeres.Client/src/app/components/DataTransition.tsx` — the cross-fade wrapper
- `ProjectCeres.Client/src/app/components/DataTransition.test.tsx` — component tests

**Modify:**
- `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx` — adopt the primitive in the subtotals card and the list body
- `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx` — add tests for the fast/slow paths and reduced motion

Each file has one responsibility and can be reasoned about in isolation. The hook handles timing only; the component handles cross-fade rendering only; consumers compose them.

---

## Task 1: `useDelayedLoading` hook

**Files:**
- Create: `ProjectCeres.Client/src/app/lib/use-delayed-loading.ts`
- Test: `ProjectCeres.Client/src/app/lib/use-delayed-loading.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Client/src/app/lib/use-delayed-loading.test.ts`:

```ts
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useDelayedLoading } from './use-delayed-loading';

describe('useDelayedLoading', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns false on first render when loading is false', () => {
    const { result } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    expect(result.current).toBe(false);
  });

  it('returns false during the delay window after loading flips true', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    expect(result.current).toBe(false);
    act(() => {
      vi.advanceTimersByTime(149);
    });
    expect(result.current).toBe(false);
  });

  it('returns true after the default 150ms delay elapses', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(150);
    });
    expect(result.current).toBe(true);
  });

  it('never returns true if loading flips false within the delay window', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(100);
    });
    rerender({ loading: false });
    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(result.current).toBe(false);
  });

  it('respects a custom delay option', () => {
    const { result, rerender } = renderHook(
      ({ loading }) => useDelayedLoading(loading, { delay: 50 }),
      { initialProps: { loading: false } },
    );
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(49);
    });
    expect(result.current).toBe(false);
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current).toBe(true);
  });

  it('resets to false immediately when loading flips back to false', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(result.current).toBe(true);
    rerender({ loading: false });
    expect(result.current).toBe(false);
  });

  it('does not throw when unmounted while a timer is pending', () => {
    const { rerender, unmount } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    unmount();
    expect(() => {
      vi.advanceTimersByTime(500);
    }).not.toThrow();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm --dir ProjectCeres.Client test -- use-delayed-loading`
Expected: FAIL — module `./use-delayed-loading` does not exist.

- [ ] **Step 3: Implement the hook**

Create `ProjectCeres.Client/src/app/lib/use-delayed-loading.ts`:

```ts
import { useEffect, useState } from 'react';

const DEFAULT_DELAY_MS = 150;

/**
 * Suppresses a "loading" indicator for a short window so fast responses
 * never flash a skeleton. Returns true only if `loading` has been true
 * continuously for `delay` ms (default 150).
 */
export function useDelayedLoading(
  loading: boolean,
  options?: { delay?: number },
): boolean {
  const delay = options?.delay ?? DEFAULT_DELAY_MS;
  const [showSkeleton, setShowSkeleton] = useState(false);

  useEffect(() => {
    if (!loading) {
      setShowSkeleton(false);
      return;
    }
    const timer = window.setTimeout(() => setShowSkeleton(true), delay);
    return () => window.clearTimeout(timer);
  }, [loading, delay]);

  return showSkeleton;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm --dir ProjectCeres.Client test -- use-delayed-loading`
Expected: PASS — all 7 tests green.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/lib/use-delayed-loading.ts ProjectCeres.Client/src/app/lib/use-delayed-loading.test.ts
git commit -m "feat(client): add useDelayedLoading hook for skeleton suppression"
```

---

## Task 2: `<DataTransition>` component

**Files:**
- Create: `ProjectCeres.Client/src/app/components/DataTransition.tsx`
- Test: `ProjectCeres.Client/src/app/components/DataTransition.test.tsx`

This component cross-fades between three slots using opacity transitions tied to design-system tokens. It keeps the previous slot mounted during the fade so the swap is visual rather than a layout pop.

- [ ] **Step 1: Write the failing tests**

Create `ProjectCeres.Client/src/app/components/DataTransition.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DataTransition } from './DataTransition';

function setupMatchMedia(matches: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

describe('DataTransition', () => {
  beforeEach(() => {
    setupMatchMedia(false);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the skeleton slot when state is "skeleton"', () => {
    render(
      <DataTransition
        state="skeleton"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('sk')).toBeInTheDocument();
  });

  it('renders the data slot when state is "data"', () => {
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('data')).toBeInTheDocument();
  });

  it('renders the error slot when state is "error"', () => {
    render(
      <DataTransition
        state="error"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    expect(screen.getByTestId('err')).toBeInTheDocument();
  });

  it('marks the active slot with data-state="active" and uses the motion duration token', () => {
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    const active = screen.getByTestId('data').parentElement!;
    expect(active.getAttribute('data-state')).toBe('active');
    expect(active.style.transitionDuration).toBe('var(--motion-duration-base)');
  });

  it('disables transitions when prefers-reduced-motion matches', () => {
    setupMatchMedia(true);
    render(
      <DataTransition
        state="data"
        skeleton={<div data-testid="sk">SK</div>}
        error={<div data-testid="err">ERR</div>}
      >
        <div data-testid="data">DATA</div>
      </DataTransition>,
    );
    const root = screen.getByTestId('data').closest('[data-data-transition]')!;
    expect(root.getAttribute('data-reduced-motion')).toBe('true');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `pnpm --dir ProjectCeres.Client test -- DataTransition`
Expected: FAIL — module `./DataTransition` does not exist.

- [ ] **Step 3: Implement the component**

Create `ProjectCeres.Client/src/app/components/DataTransition.tsx`:

```tsx
import type { CSSProperties, ReactNode } from 'react';
import { useEffect, useRef, useState } from 'react';
import { useMediaQuery } from '../lib/use-media-query';

export type DataTransitionState = 'skeleton' | 'data' | 'error';

interface DataTransitionProps {
  state: DataTransitionState;
  skeleton: ReactNode;
  error: ReactNode;
  children: ReactNode;
}

const TRANSITION_MS = 180;

/**
 * Cross-fades between three slots (skeleton / error / data) using the
 * design-system motion tokens. The previous slot stays mounted for one
 * transition cycle so the swap is a visual fade rather than a pop.
 *
 * Respects prefers-reduced-motion: when matched, the transition collapses
 * to an instant swap.
 */
export function DataTransition({
  state,
  skeleton,
  error,
  children,
}: DataTransitionProps) {
  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const [previousState, setPreviousState] = useState<DataTransitionState | null>(null);
  const prevRef = useRef(state);

  useEffect(() => {
    if (prevRef.current === state) return;
    if (reducedMotion) {
      prevRef.current = state;
      setPreviousState(null);
      return;
    }
    setPreviousState(prevRef.current);
    prevRef.current = state;
    const timer = window.setTimeout(() => setPreviousState(null), TRANSITION_MS);
    return () => window.clearTimeout(timer);
  }, [state, reducedMotion]);

  function slotFor(s: DataTransitionState): ReactNode {
    if (s === 'skeleton') return skeleton;
    if (s === 'error') return error;
    return children;
  }

  const transitionStyle: CSSProperties = reducedMotion
    ? {}
    : {
        transitionProperty: 'opacity',
        transitionDuration: 'var(--motion-duration-base)',
        transitionTimingFunction: 'var(--motion-easing-standard)',
      };

  return (
    <div
      data-data-transition
      data-reduced-motion={reducedMotion ? 'true' : 'false'}
      style={{ position: 'relative' }}
    >
      <div
        data-state="active"
        style={{ ...transitionStyle, opacity: 1 }}
      >
        {slotFor(state)}
      </div>
      {previousState !== null && previousState !== state && (
        <div
          data-state="leaving"
          aria-hidden="true"
          style={{
            ...transitionStyle,
            opacity: 0,
            position: 'absolute',
            inset: 0,
            pointerEvents: 'none',
          }}
        >
          {slotFor(previousState)}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pnpm --dir ProjectCeres.Client test -- DataTransition`
Expected: PASS — all 5 tests green.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/components/DataTransition.tsx ProjectCeres.Client/src/app/components/DataTransition.test.tsx
git commit -m "feat(client): add DataTransition cross-fade component"
```

---

## Task 3: Adopt `<DataTransition>` in the Accounts subtotals card

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx:74`

The subtotals strip currently pops in via `{list.data ? <AccountCurrencySubtotals ... /> : null}`. Wrap it so it fades in instead, with no skeleton flash for fast responses. The existing test at line 131 (`hides the per-currency subtotal strip when only one currency`) must still pass — the data-arrival behaviour is unchanged, only the visual transition is new.

- [ ] **Step 1: Read current state**

Confirm line 74 of `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx` reads:

```tsx
      {list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}
```

- [ ] **Step 2: Add imports**

Edit the imports block at the top of `AccountsLayout.tsx` to add the two new pieces. Find:

```tsx
import { CardError } from '../../components/CardError';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
```

Replace with:

```tsx
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi, type UseApiResult } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
```

- [ ] **Step 3: Wrap the subtotals card with `<DataTransition>`**

Replace line 74:

```tsx
      {list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}
```

with:

```tsx
      <SubtotalsArea list={list} />
```

Then add the `SubtotalsArea` component above `AccountsBody` (after the `AccountsLayout` function, before the `AccountsBody` function declaration):

```tsx
function SubtotalsArea({ list }: { list: UseApiResult<AccountListItemDto[]> }) {
  const showSkeleton = useDelayedLoading(list.loading && !list.data);
  if (!list.data && !showSkeleton) {
    return null;
  }
  const state: DataTransitionState =
    showSkeleton && !list.data ? 'skeleton' : 'data';
  return (
    <DataTransition
      state={state}
      skeleton={<div data-testid="subtotals-skeleton" className="h-12 w-full" />}
      error={null}
    >
      {list.data ? <AccountCurrencySubtotals rows={list.data} /> : null}
    </DataTransition>
  );
}
```

The `if (!list.data && !showSkeleton) return null` guard preserves the existing behaviour: nothing renders during the first 150 ms of a cold load (no skeleton, no card), so the page header doesn't shift down.

- [ ] **Step 4: Run the existing Accounts test suite to make sure nothing regressed**

Run: `pnpm --dir ProjectCeres.Client test -- AccountsLayout`
Expected: PASS — all 12 existing tests still green.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx
git commit -m "feat(accounts): cross-fade currency subtotals card on data load"
```

---

## Task 4: Adopt `<DataTransition>` in the Accounts list body

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx` (the `AccountsBody` function, lines 113–177)

Replace the manual `if (loading) return <Skeleton />` / `if (error) return <CardError />` chain with a single `<DataTransition>`. Preserve the `data-testid="accounts-skeleton"` so existing tests continue to find it; preserve all existing data branches (empty state, search-empty state, table) by treating "data has arrived" as a single slot.

- [ ] **Step 1: Read current state**

Confirm `AccountsBody` (lines 113–177) matches the existing implementation — manual skeleton/error/data branching with empty-state handling for both first-run and search-empty cases.

- [ ] **Step 2: Replace `AccountsBody` with the `<DataTransition>` version**

Replace the entire `AccountsBody` function (lines 113–177) with:

```tsx
function AccountsBody({
  list, allList, query, onClearSearch,
}: {
  list: UseApiResult<AccountListItemDto[]>;
  allList: UseApiResult<AccountListItemDto[]>;
  query: string;
  onClearSearch: () => void;
}) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    return [...list.data]
      .filter((row) => row.name.toLowerCase().includes(lower))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [list.data, lower]);

  const showSkeleton = useDelayedLoading(list.loading && !list.data);

  let state: DataTransitionState;
  if (showSkeleton && !list.data) {
    state = 'skeleton';
  } else if (list.error && !list.data) {
    state = 'error';
  } else {
    state = 'data';
  }

  const skeleton = (
    <div data-testid="accounts-skeleton" className="space-y-2 py-2">
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
    </div>
  );

  const errorSlot = <CardError section="Accounts" onRetry={list.refetch} />;

  return (
    <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      <AccountsDataView
        list={list}
        allList={allList}
        sorted={sorted}
        query={query}
        onClearSearch={onClearSearch}
      />
    </DataTransition>
  );
}

function AccountsDataView({
  list, allList, sorted, query, onClearSearch,
}: {
  list: UseApiResult<AccountListItemDto[]>;
  allList: UseApiResult<AccountListItemDto[]>;
  sorted: AccountListItemDto[];
  query: string;
  onClearSearch: () => void;
}) {
  if (!list.data) return null;

  if (sorted.length === 0 && query === '') {
    const totalAccountsExist = (allList.data?.length ?? 0) > 0;
    if (!totalAccountsExist) {
      return (
        <div className="px-3 py-12 text-center space-y-3">
          <h2 className="text-lg font-semibold">No accounts yet</h2>
          <p className="text-sm text-muted-foreground">
            Create your first account to start tracking.
          </p>
          <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New account</Link>} />
        </div>
      );
    }
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No accounts.
      </div>
    );
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No accounts match '{query}'.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>
          Clear search
        </Button>
      </div>
    );
  }

  return <AccountsTable rows={sorted} onChanged={() => { list.refetch(); allList.refetch(); }} />;
}
```

The `AccountsDataView` extraction is what makes the `data` slot a single subtree the cross-fade can swap into. The state-derivation rules:

- `skeleton` only when delay has elapsed AND no stale data is available
- `error` only when there's an error AND no stale data is available (refetch errors keep the stale table visible)
- `data` otherwise — including the empty-state and search-empty-state branches inside `AccountsDataView`

- [ ] **Step 3: Run the existing Accounts test suite**

Run: `pnpm --dir ProjectCeres.Client test -- AccountsLayout`
Expected: PASS — all 12 existing tests still green. The "renders skeleton while loading" test passes because `useDelayedLoading` is called with `loading=true` and the test uses `screen.getByTestId('accounts-skeleton')` synchronously; in tests using real timers the `useEffect` schedules the timeout but the test inspects the DOM before it fires, so the skeleton wouldn't be visible. This will need adjustment — see next step.

- [ ] **Step 4: Update the existing "renders skeleton while loading" test**

The existing test at `AccountsLayout.test.tsx` line 48–52:

```tsx
it('renders skeleton while loading', () => {
  mockFetch.mockImplementation(() => new Promise(() => {}));
  renderAt('/accounts');
  expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument();
});
```

is now wrong: with the 150 ms delay, the skeleton no longer appears synchronously. Replace the test body with:

```tsx
it('renders skeleton after the delay window when loading is slow', async () => {
  mockFetch.mockImplementation(() => new Promise(() => {}));
  renderAt('/accounts');
  expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
  await waitFor(
    () => expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument(),
    { timeout: 500 },
  );
});
```

- [ ] **Step 5: Run the test suite again**

Run: `pnpm --dir ProjectCeres.Client test -- AccountsLayout`
Expected: PASS — all 12 tests green, including the rewritten skeleton test.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/accounts/AccountsLayout.tsx ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx
git commit -m "feat(accounts): cross-fade list body skeleton/error/data states"
```

---

## Task 5: Add fast-path and slow-path tests to `AccountsLayout.test.tsx`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx`

These tests cover the new behaviour: fast responses skip the skeleton entirely; slow responses show the skeleton then the table.

- [ ] **Step 1: Add the fast-response test**

Append a new `it` block to the `describe('AccountsLayout', ...)` block in `AccountsLayout.test.tsx` (after the existing tests, before the closing brace):

```tsx
  it('does not flash the skeleton when the response is faster than the delay window', async () => {
    renderAt('/accounts');
    expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
    await screen.findByText('Cash');
    expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
  });
```

This works because the mock `fetch` resolves on the next microtask — well under 150 ms. The skeleton's `setTimeout` is cleared before it can fire.

- [ ] **Step 2: Add the slow-response test**

Append another `it` block:

```tsx
  it('shows the skeleton then cross-fades to data when the response is slow', async () => {
    let resolveFetch: (value: { ok: true; status: 200; json: () => Promise<unknown> }) => void;
    const slowResponse = new Promise<{ ok: true; status: 200; json: () => Promise<unknown> }>(
      (resolve) => { resolveFetch = resolve; },
    );
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') return slowResponse;
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    });

    renderAt('/accounts');

    await waitFor(
      () => expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument(),
      { timeout: 500 },
    );

    resolveFetch!({
      ok: true,
      status: 200,
      json: async () => allRows.filter((r) => r.isActive),
    });

    await screen.findByText('Cash');
  });
```

- [ ] **Step 3: Add the reduced-motion test**

Append a third `it` block:

```tsx
  it('disables the cross-fade when prefers-reduced-motion matches', async () => {
    const matchMediaSpy = vi.fn().mockImplementation((query: string) => ({
      matches: query === '(prefers-reduced-motion: reduce)',
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: matchMediaSpy,
    });

    renderAt('/accounts');
    await screen.findByText('Cash');
    const transitions = document.querySelectorAll('[data-data-transition]');
    expect(transitions.length).toBeGreaterThan(0);
    transitions.forEach((node) => {
      expect(node.getAttribute('data-reduced-motion')).toBe('true');
    });
  });
```

- [ ] **Step 4: Run the full Accounts test suite**

Run: `pnpm --dir ProjectCeres.Client test -- AccountsLayout`
Expected: PASS — 15 tests green (12 existing + 3 new).

- [ ] **Step 5: Run the full client test suite to catch any unrelated regressions**

Run: `pnpm --dir ProjectCeres.Client test`
Expected: PASS — all client tests green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/features/accounts/AccountsLayout.test.tsx
git commit -m "test(accounts): cover fast-skip, slow-fade, and reduced-motion paths"
```

---

## Task 6: Browser verification on the Accounts page

**Files:** none (manual verification per `feedback_ui_work_checklist`)

This task validates the felt experience in a real browser. Per the design system's UX/UI verification checklist, we cannot ship a frontend change without confirming it in-browser.

- [ ] **Step 1: Build and start the app**

Run in two terminals:

```bash
dotnet run --project ProjectCeres
```

```bash
pnpm --dir ProjectCeres.Client dev
```

Wait for both to be ready.

- [ ] **Step 2: Verify the fast path (no throttling)**

Open the Vite dev URL → navigate to `/accounts` (or click Accounts in the sidebar). Observe: the table fades in smoothly. No skeleton flash should be visible.

- [ ] **Step 3: Verify the slow path (Slow 3G throttling)**

Open Chrome DevTools → Network tab → Throttling dropdown → "Slow 3G". Reload `/accounts`. Observe: nothing renders for ~150 ms, then the skeleton fades in, then cross-fades to the table when the response arrives. No layout pop.

- [ ] **Step 4: Verify the stale-data path (refetch keeps table visible)**

Reset throttling to "No throttling". With the table visible, toggle the "Include archived" switch. Observe: the existing table stays visible during the refetch (no skeleton flash). The archived row appears once the new response lands.

- [ ] **Step 5: Verify the error path**

In DevTools → Network → right-click a request to `/api/accounts` → "Block request URL". Reload `/accounts`. Observe: the error card appears via cross-fade (no pop). Click Retry; on success, the error card cross-fades to the table.

- [ ] **Step 6: Verify reduced-motion**

macOS: System Settings → Accessibility → Display → toggle "Reduce motion" ON. Reload `/accounts`. Repeat the fast and slow paths — the fast path should still skip the skeleton (instant data render), the slow path should swap instantly between skeleton and table with no opacity animation.

Turn Reduce motion back OFF when finished.

- [ ] **Step 7: Verify mobile (375 px)**

DevTools → toggle device toolbar → set viewport to 375 px wide. Reload `/accounts` and confirm the fast and slow paths look correct at this width.

- [ ] **Step 8: Commit nothing — report findings**

If any path looks wrong, file the specific issue (which path, what was observed vs. expected) and stop. If all paths look right, the implementation is complete.

---

## Out of scope (separate follow-up plan)

Rolling `<DataTransition>` + `useDelayedLoading` out to other pages and dashboard cards. Each will follow the Accounts pattern; the rollout is mechanical once the primitive is proven on Accounts.
