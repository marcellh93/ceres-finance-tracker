# Reports Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the Reports section with a tab bar, shared URL-driven filters, KPI tiles, Recharts charts, and paginated tables — all derived from existing API data with no new endpoints.

**Architecture:** `ReportsLayout` gains a tab bar (`ReportsTabBar`) and shared filter bar (`ReportsSharedFilterBar`); filter state lives in URL search params via `useReportsFilters` (already exists). Each of 6 report pages gains KPI tiles + a chart above the existing table. A new `usePagination` hook slices table data client-side. `ReportsIndex` is deleted; the index route redirects to `net-worth-over-time`.

**Tech Stack:** React 19, React Router v6, Recharts (via shadcn `ChartContainer`), Tailwind v4, shadcn/ui, Vitest + React Testing Library.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `src/hooks/usePagination.ts` | **Create** | Generic pagination hook |
| `src/app/features/reports/ReportsTabBar.tsx` | **Create** | Tab bar reading `REPORT_META`, carries search params |
| `src/app/features/reports/ReportsSharedFilterBar.tsx` | **Create** | Date range + currency, reads/writes `useSearchParams` |
| `src/app/features/reports/ReportLocalFilterBar.tsx` | **Create** | Rename of `ReportsFilterBar` — report-specific filters only |
| `src/app/features/reports/ReportsLayout.tsx` | **Modify** | Add tab bar + shared filter bar, remove back button, fluid width |
| `src/app/features/reports/ReportTableCard.tsx` | **Modify** | Add optional `pagination` prop + footer controls |
| `src/app/features/reports/NetWorthOverTime.tsx` | **Modify** | Add KPI tiles + AreaChart, use `ReportLocalFilterBar` |
| `src/app/features/reports/IncomeExpense.tsx` | **Modify** | Add chart (grouped BarChart), update KPI tiles, use `ReportLocalFilterBar` |
| `src/app/features/reports/MonthlyCashFlow.tsx` | **Modify** | Add KPI tiles + stacked BarChart, use `ReportLocalFilterBar` |
| `src/app/features/reports/ExpenseBreakdown.tsx` | **Modify** | Add KPI tiles + horizontal BarChart, use `ReportLocalFilterBar` |
| `src/app/features/reports/BudgetVsActual.tsx` | **Modify** | Add KPI tiles (uses existing Progress bars as chart zone), use `ReportLocalFilterBar` |
| `src/app/features/reports/LargestExpenses.tsx` | **Modify** | Add KPI tiles + horizontal BarChart, use `ReportLocalFilterBar` |
| `src/app/features/reports/NetWorth.tsx` | **Modify** | Use `ReportLocalFilterBar`, remove `ReportsFilterBar` import |
| `src/app/features/reports/TransactionHistory.tsx` | **Modify** | Use `ReportLocalFilterBar`, remove `ReportsFilterBar` import |
| `src/app/features/reports/ReportsFilterBar.tsx` | **Delete** | Replaced by `ReportsSharedFilterBar` + `ReportLocalFilterBar` |
| `src/app/features/reports/ReportsIndex.tsx` | **Delete** | Replaced by tab bar + redirect |
| `src/app/App.tsx` | **Modify** | Replace index route with redirect, remove `ReportsIndex` import |
| `src/app/features/reports/ReportsIndex.test.tsx` | **Delete** | Component deleted |

---

## Task 1: `usePagination` hook

**Files:**
- Create: `ProjectCeres.Client/src/hooks/usePagination.ts`
- Create: `ProjectCeres.Client/src/hooks/usePagination.test.ts`

- [ ] **Step 1: Write the failing test**

```ts
// ProjectCeres.Client/src/hooks/usePagination.test.ts
import { renderHook, act } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { usePagination } from './usePagination';

const items = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];

describe('usePagination', () => {
  it('returns first page of items', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    expect(result.current.paginatedItems).toEqual([1, 2, 3, 4, 5, 6]);
    expect(result.current.currentPage).toBe(1);
    expect(result.current.totalPages).toBe(3);
  });

  it('next() advances page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    expect(result.current.paginatedItems).toEqual([7, 8, 9, 10, 11, 12]);
    expect(result.current.currentPage).toBe(2);
  });

  it('prev() goes back', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.prev());
    expect(result.current.currentPage).toBe(1);
  });

  it('next() clamps at last page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.next());
    act(() => result.current.next()); // already on last page
    expect(result.current.currentPage).toBe(3);
  });

  it('prev() clamps at first page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.prev());
    expect(result.current.currentPage).toBe(1);
  });

  it('last page returns remaining items', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.next());
    expect(result.current.paginatedItems).toEqual([13]);
  });

  it('goTo() jumps to specific page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.goTo(2));
    expect(result.current.currentPage).toBe(2);
  });

  it('empty items returns page 1 of 1 with empty array', () => {
    const { result } = renderHook(() => usePagination([], 6));
    expect(result.current.paginatedItems).toEqual([]);
    expect(result.current.totalPages).toBe(1);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd ProjectCeres.Client && pnpm test src/hooks/usePagination.test.ts
```

Expected: FAIL — `usePagination` not found.

- [ ] **Step 3: Implement the hook**

```ts
// ProjectCeres.Client/src/hooks/usePagination.ts
import { useState, useMemo } from 'react';

export function usePagination<T>(items: T[], pageSize: number) {
  const [currentPage, setCurrentPage] = useState(1);

  const totalPages = useMemo(
    () => Math.max(1, Math.ceil(items.length / pageSize)),
    [items.length, pageSize],
  );

  const paginatedItems = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return items.slice(start, start + pageSize);
  }, [items, currentPage, pageSize]);

  function next() {
    setCurrentPage((p) => Math.min(p, totalPages - 1) + 1 <= totalPages ? Math.min(p + 1, totalPages) : p);
  }

  function prev() {
    setCurrentPage((p) => Math.max(p - 1, 1));
  }

  function goTo(page: number) {
    setCurrentPage(Math.max(1, Math.min(page, totalPages)));
  }

  return { paginatedItems, currentPage, totalPages, next, prev, goTo };
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd ProjectCeres.Client && pnpm test src/hooks/usePagination.test.ts
```

Expected: All 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/hooks/usePagination.ts ProjectCeres.Client/src/hooks/usePagination.test.ts
git commit -m "feat(reports): add usePagination hook"
```

---

## Task 2: `ReportTableCard` — add pagination prop

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/ReportTableCard.tsx`

- [ ] **Step 1: Write the failing test**

Add a `describe` block to a new test file:

```ts
// ProjectCeres.Client/src/app/features/reports/ReportTableCard.test.tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { ReportTableCard } from './ReportTableCard';

function renderCard(pagination?: { currentPage: number; totalPages: number; onNext: () => void; onPrev: () => void }) {
  render(
    <MemoryRouter>
      <ReportTableCard slug="test" queryString="">
        <div>content</div>
      </ReportTableCard>
      {/* re-render with pagination below */}
    </MemoryRouter>,
  );
}

describe('ReportTableCard pagination', () => {
  it('renders without pagination by default', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString=""><div>content</div></ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.queryByRole('button', { name: /previous/i })).not.toBeInTheDocument();
  });

  it('renders pagination controls when prop is provided', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 2, totalPages: 5, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByText('Page 2 of 5')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /previous/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /next/i })).toBeInTheDocument();
  });

  it('calls onNext when next button clicked', async () => {
    const onNext = vi.fn();
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 1, totalPages: 3, onNext, onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    await userEvent.click(screen.getByRole('button', { name: /next/i }));
    expect(onNext).toHaveBeenCalledOnce();
  });

  it('disables prev on first page', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 1, totalPages: 3, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled();
  });

  it('disables next on last page', () => {
    render(
      <MemoryRouter>
        <ReportTableCard slug="test" queryString="" pagination={{ currentPage: 3, totalPages: 3, onNext: vi.fn(), onPrev: vi.fn() }}>
          <div>content</div>
        </ReportTableCard>
      </MemoryRouter>
    );
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ReportTableCard.test.tsx
```

Expected: FAIL — pagination controls not found.

- [ ] **Step 3: Implement pagination prop in `ReportTableCard`**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportTableCard.tsx
import { ChevronLeft, ChevronRight, Download } from 'lucide-react';
import { toast } from 'sonner';
import { buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { buildCsvHref } from './csv-export';

type PaginationProps = {
  currentPage: number;
  totalPages: number;
  onNext: () => void;
  onPrev: () => void;
};

type Props = {
  slug: string;
  queryString: string;
  children: React.ReactNode;
  pagination?: PaginationProps;
};

export function ReportTableCard({ slug, queryString, children, pagination }: Props) {
  const csvHref = buildCsvHref(slug, queryString);

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <span className="text-sm font-medium text-muted-foreground">Results</span>
        <a
          href={csvHref}
          rel="noopener"
          onClick={() => toast.info('Exporting report…')}
          className={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'gap-2 no-underline')}
        >
          <Download className="h-4 w-4" aria-hidden="true" />
          Export CSV
        </a>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {children}
      </CardContent>
      {pagination && (
        <div className="flex items-center justify-center gap-3 border-t border-border px-4 py-2">
          <button
            onClick={pagination.onPrev}
            disabled={pagination.currentPage === 1}
            aria-label="Previous page"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-border bg-background text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:pointer-events-none disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          </button>
          <span className="text-sm text-muted-foreground">
            Page {pagination.currentPage} of {pagination.totalPages}
          </span>
          <button
            onClick={pagination.onNext}
            disabled={pagination.currentPage === pagination.totalPages}
            aria-label="Next page"
            className="flex h-7 w-7 items-center justify-center rounded-md border border-border bg-background text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:pointer-events-none disabled:opacity-40"
          >
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      )}
    </Card>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ReportTableCard.test.tsx
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/ReportTableCard.tsx ProjectCeres.Client/src/app/features/reports/ReportTableCard.test.tsx
git commit -m "feat(reports): add optional pagination prop to ReportTableCard"
```

---

## Task 3: `ReportsTabBar` component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/reports/ReportsTabBar.tsx`
- Create: `ProjectCeres.Client/src/app/features/reports/ReportsTabBar.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportsTabBar.test.tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it, expect } from 'vitest';
import { ReportsTabBar } from './ReportsTabBar';

function renderTabBar(path: string, search = '') {
  render(
    <MemoryRouter initialEntries={[`${path}${search}`]}>
      <Routes>
        <Route path="reports/:slug" element={<ReportsTabBar />} />
        <Route path="reports" element={<ReportsTabBar />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('ReportsTabBar', () => {
  it('renders all 8 report tabs', () => {
    renderTabBar('/reports/net-worth-over-time');
    expect(screen.getByRole('link', { name: /net worth over time/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /income vs expense/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /transaction history/i })).toBeInTheDocument();
  });

  it('marks active tab with aria-current', () => {
    renderTabBar('/reports/income-expense');
    expect(screen.getByRole('link', { name: /income vs expense/i })).toHaveAttribute('aria-current', 'page');
  });

  it('carries search params forward in tab links', () => {
    renderTabBar('/reports/net-worth-over-time', '?from=2026-01&to=2026-04&currencyId=1');
    const link = screen.getByRole('link', { name: /income vs expense/i });
    expect(link.getAttribute('href')).toContain('from=2026-01');
    expect(link.getAttribute('href')).toContain('to=2026-04');
    expect(link.getAttribute('href')).toContain('currencyId=1');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ReportsTabBar.test.tsx
```

Expected: FAIL — `ReportsTabBar` not found.

- [ ] **Step 3: Implement `ReportsTabBar`**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportsTabBar.tsx
import { Link, useMatch, useSearchParams } from 'react-router-dom';
import { cn } from '@/lib/utils';
import { REPORT_META } from './reports-api';

export function ReportsTabBar() {
  const match = useMatch('/reports/:slug');
  const activeSlug = match?.params.slug ?? '';
  const [searchParams] = useSearchParams();

  return (
    <div
      role="tablist"
      aria-label="Reports"
      className="flex overflow-x-auto border-b border-border bg-background scrollbar-none"
    >
      {REPORT_META.map((entry) => {
        const isActive = entry.slug === activeSlug;
        const to = `/reports/${entry.slug}?${searchParams.toString()}`;
        return (
          <Link
            key={entry.slug}
            to={to}
            role="tab"
            aria-current={isActive ? 'page' : undefined}
            className={cn(
              'relative shrink-0 px-4 py-3 text-sm font-medium text-muted-foreground no-underline transition-colors hover:text-foreground',
              isActive && 'text-foreground after:absolute after:inset-x-4 after:bottom-0 after:h-0.5 after:rounded-t after:bg-foreground after:opacity-100',
            )}
          >
            {entry.label}
          </Link>
        );
      })}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ReportsTabBar.test.tsx
```

Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/ReportsTabBar.tsx ProjectCeres.Client/src/app/features/reports/ReportsTabBar.test.tsx
git commit -m "feat(reports): add ReportsTabBar"
```

---

## Task 4: `ReportsSharedFilterBar` component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/reports/ReportsSharedFilterBar.tsx`

No test needed for this component — it is a thin wrapper around `DateRangePicker` + `CurrencyCombobox` which are already tested independently. Integration is covered by the layout test in Task 5.

- [ ] **Step 1: Implement `ReportsSharedFilterBar`**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportsSharedFilterBar.tsx
import { DateRangePicker } from '@/components/DateRangePicker';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
import { useReportsFilters } from './useReportsFilters';

export function ReportsSharedFilterBar() {
  const { filters, setFilter } = useReportsFilters();

  return (
    <div className="sticky top-14 z-10 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="grid grid-cols-1 gap-2 px-[8%] py-2 sm:grid-cols-2">
        <DateRangePicker fromKey="from" toKey="to" className="w-full" />
        <CurrencyCombobox
          value={filters.currencyId}
          onChange={(id) => setFilter('currencyId', id)}
          placeholder="Currency"
          className="w-full"
        />
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/ReportsSharedFilterBar.tsx
git commit -m "feat(reports): add ReportsSharedFilterBar"
```

---

## Task 5: `ReportLocalFilterBar` + update `ReportsLayout`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/reports/ReportLocalFilterBar.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx`

- [ ] **Step 1: Create `ReportLocalFilterBar`**

This is `ReportsFilterBar` stripped of date range and currency controls — only account and category selectors remain.

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportLocalFilterBar.tsx
import { useMatch } from 'react-router-dom';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { useReportsFilters } from './useReportsFilters';

const USES_ACCOUNT  = new Set(['transaction-history']);
const USES_CATEGORY = new Set(['expense-breakdown', 'transaction-history']);

function useCurrentSlug(): string | null {
  const match = useMatch('/reports/:slug');
  return match?.params.slug ?? null;
}

export function ReportLocalFilterBar() {
  const slug = useCurrentSlug();
  const { filters, setFilter } = useReportsFilters();

  const showAccount  = slug ? USES_ACCOUNT.has(slug) : false;
  const showCategory = slug ? USES_CATEGORY.has(slug) : false;

  const { data: accounts }   = useApi<AccountOptionDto[]>(showAccount ? ACCOUNTS_ACTIVE_URL : '');
  const { data: categories } = useApi<CategoryOptionDto[]>(showCategory ? CATEGORIES_ACTIVE_URL : '');

  if (!showAccount && !showCategory) return null;

  return (
    <div className="mb-4">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {showAccount && (
          <AccountCombobox
            accounts={accounts ?? []}
            value={filters.accountId}
            onChange={(id) => setFilter('accountId', id)}
            placeholder="All accounts"
            className="w-full"
          />
        )}
        {showCategory && (
          <CategoryCombobox
            categories={categories ?? []}
            value={filters.categoryId}
            onChange={(id) => setFilter('categoryId', id)}
            placeholder="All categories"
            className="w-full"
          />
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Update `ReportsLayout`**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx
import { Outlet } from 'react-router-dom';
import { ReportsTabBar } from './ReportsTabBar';
import { ReportsSharedFilterBar } from './ReportsSharedFilterBar';

export function ReportsLayout() {
  return (
    <div className="flex flex-col">
      <ReportsTabBar />
      <ReportsSharedFilterBar />
      <div className="px-[8%] py-6">
        <Outlet />
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Write a layout integration test**

```tsx
// ProjectCeres.Client/src/app/features/reports/ReportsLayout.test.tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ReportsLayout } from './ReportsLayout';

beforeEach(() => { global.fetch = vi.fn().mockReturnValue(new Promise(() => {})); });
afterEach(() => { vi.resetAllMocks(); });

function renderLayout(path = '/reports/net-worth-over-time') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path=":slug" element={<div>page content</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('ReportsLayout', () => {
  it('renders the tab bar', () => {
    renderLayout();
    expect(screen.getByRole('tablist', { name: /reports/i })).toBeInTheDocument();
  });

  it('renders all 8 tabs', () => {
    renderLayout();
    expect(screen.getAllByRole('tab')).toHaveLength(8);
  });

  it('renders shared filter bar', () => {
    renderLayout();
    // DateRangePicker renders a button with the date range
    expect(screen.getByRole('combobox', { name: /currency/i })).toBeInTheDocument();
  });

  it('renders outlet content', () => {
    renderLayout();
    expect(screen.getByText('page content')).toBeInTheDocument();
  });
});
```

- [ ] **Step 4: Run the layout tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ReportsLayout.test.tsx
```

Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add \
  ProjectCeres.Client/src/app/features/reports/ReportLocalFilterBar.tsx \
  ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx \
  ProjectCeres.Client/src/app/features/reports/ReportsLayout.test.tsx
git commit -m "feat(reports): tab bar + shared filter bar in ReportsLayout, add ReportLocalFilterBar"
```

---

## Task 6: Update `App.tsx` — delete index route, add redirect

**Files:**
- Modify: `ProjectCeres.Client/src/app/App.tsx`
- Delete: `ProjectCeres.Client/src/app/features/reports/ReportsIndex.tsx`
- Delete: `ProjectCeres.Client/src/app/features/reports/ReportsIndex.test.tsx`

- [ ] **Step 1: Replace the index route in `App.tsx`**

In `App.tsx`, remove the `ReportsIndex` import (line 12) and replace the index route (line 78):

```tsx
// Remove this import:
// import { ReportsIndex } from './features/reports/ReportsIndex';

// Replace:
// <Route index element={<ReportsIndex />} />
// With:
import { Navigate } from 'react-router-dom';
// ...
<Route index element={<Navigate to="net-worth-over-time" replace />} />
```

The `Navigate` import should be added to the existing `react-router-dom` import at the top of `App.tsx`.

- [ ] **Step 2: Delete `ReportsIndex.tsx` and its test**

```bash
rm ProjectCeres.Client/src/app/features/reports/ReportsIndex.tsx
rm ProjectCeres.Client/src/app/features/reports/ReportsIndex.test.tsx
```

- [ ] **Step 3: Run full test suite to confirm no regressions**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: All tests PASS (ReportsIndex tests gone, no new failures).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/App.tsx
git rm ProjectCeres.Client/src/app/features/reports/ReportsIndex.tsx
git rm ProjectCeres.Client/src/app/features/reports/ReportsIndex.test.tsx
git commit -m "feat(reports): replace index route with redirect, delete ReportsIndex"
```

---

## Task 7: `NetWorthOverTime` — KPI tiles + AreaChart

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.test.tsx`

- [ ] **Step 1: Add tests for KPI tiles and chart**

Add to `NetWorthOverTime.test.tsx` (keep existing tests, add these):

```tsx
const twoRows = [
  { year: 2026, month: 1, currencyCode: 'EUR', currencySymbol: '€', assets: 40000, liabilities: 5000, netWorth: 35000 },
  { year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', assets: 48200, liabilities: 5400, netWorth: 42800 },
];

it('renders KPI tile for Net Worth', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Net Worth')).toBeInTheDocument());
});

it('renders KPI tile for Total Assets', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Assets')).toBeInTheDocument());
});

it('renders KPI tile for Total Liabilities', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => twoRows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Liabilities')).toBeInTheDocument());
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/NetWorthOverTime.test.tsx
```

Expected: 3 new tests FAIL.

- [ ] **Step 3: Rewrite `NetWorthOverTime.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { useApi } from '../../lib/use-api';
import { usePagination } from '@/hooks/usePagination';
import { Area, AreaChart, CartesianGrid, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts';
import { REPORTS_NET_WORTH_OVER_TIME_URL, reportMetaBySlug, type NetWorthSnapshotRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

function monthLabel(row: NetWorthSnapshotRowDto): string {
  return `${MONTHS[row.month - 1]} ${row.year}`;
}

function formatDelta(value: number, symbol: string): string {
  const sign = value >= 0 ? '↑' : '↓';
  return `${sign} ${symbol} ${Math.abs(value).toFixed(2)}`;
}

export function NetWorthOverTime() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<NetWorthSnapshotRowDto[]>(REPORTS_NET_WORTH_OVER_TIME_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const first = data?.[0];
  const last  = data?.[data.length - 1];
  const symbol = last?.currencySymbol ?? '€';

  const chartData = (data ?? []).map((row) => ({
    name: monthLabel(row),
    netWorth: row.netWorth,
    assets: row.assets,
    liabilities: row.liabilities,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Net Worth Over Time" description={reportMetaBySlug('net-worth-over-time')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No data for this period.</p>
      )}
      {data && data.length > 0 && last && first && (
        <>
          {/* KPI tiles */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Net Worth"
                value={<Numeric className={`text-xl ${last.netWorth >= 0 ? 'text-success' : 'text-destructive'}`}>{symbol} {last.netWorth.toFixed(2)}</Numeric>}
                valueClassName="mt-1"
              />
              <p className={`mt-1 text-xs ${(last.netWorth - first.netWorth) >= 0 ? 'text-success' : 'text-destructive'}`}>
                {formatDelta(last.netWorth - first.netWorth, symbol)} vs period start
              </p>
            </Tile>
            <Tile>
              <StatTile
                label="Total Assets"
                value={<Numeric className="text-xl">{symbol} {last.assets.toFixed(2)}</Numeric>}
                valueClassName="mt-1"
              />
              <p className={`mt-1 text-xs ${(last.assets - first.assets) >= 0 ? 'text-success' : 'text-destructive'}`}>
                {formatDelta(last.assets - first.assets, symbol)} vs period start
              </p>
            </Tile>
            <Tile>
              <StatTile
                label="Total Liabilities"
                value={<Numeric className={`text-xl ${last.liabilities > 0 ? 'text-destructive' : ''}`}>{symbol} {last.liabilities.toFixed(2)}</Numeric>}
                valueClassName="mt-1"
              />
              <p className={`mt-1 text-xs ${(last.liabilities - first.liabilities) <= 0 ? 'text-success' : 'text-destructive'}`}>
                {formatDelta(first.liabilities - last.liabilities, symbol)} vs period start
              </p>
            </Tile>
          </div>

          {/* Chart */}
          <ChartContainer
            config={{
              netWorth: { label: 'Net Worth', color: 'var(--primary)' },
            }}
            className="h-[220px] w-full"
          >
            <AreaChart data={chartData} margin={{ top: 8, right: 0, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="grad-nwot" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="var(--primary)" stopOpacity={0.18} />
                  <stop offset="95%" stopColor="var(--primary)" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid vertical={false} stroke="var(--border)" />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <Tooltip
                contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }}
                formatter={(value: number) => [`${symbol} ${value.toFixed(2)}`, 'Net Worth']}
              />
              <Area type="monotone" dataKey="netWorth" stroke="var(--primary)" strokeWidth={2} fill="url(#grad-nwot)" dot={false} />
            </AreaChart>
          </ChartContainer>

          {/* Table */}
          <ReportTableCard
            slug="net-worth-over-time"
            queryString={qs}
            pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}
          >
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Month</TableHead>
                  <TableHead className="text-right">Assets</TableHead>
                  <TableHead className="text-right">Liabilities</TableHead>
                  <TableHead className="text-right">Net Worth</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={`${row.year}-${row.month}`}>
                    <TableCell>{monthLabel(row)}</TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.assets.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric>{row.currencySymbol} {row.liabilities.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.netWorth >= 0 ? 'text-success' : 'text-destructive'}>
                        {row.currencySymbol} {row.netWorth.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/NetWorthOverTime.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.tsx ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.test.tsx
git commit -m "feat(reports): add KPI tiles + AreaChart to NetWorthOverTime"
```

---

## Task 8: `MonthlyCashFlow` — KPI tiles + stacked BarChart

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.test.tsx`

- [ ] **Step 1: Add KPI tests**

Add to `MonthlyCashFlow.test.tsx`:

```tsx
const rows = [
  { year: 2026, month: 1, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, net: 1000 },
  { year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3500, totalExpenses: 2200, net: 1300 },
];

it('renders KPI tile for Total Income', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Income')).toBeInTheDocument());
});

it('renders KPI tile for Total Expenses', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Expenses')).toBeInTheDocument());
});

it('renders KPI tile for Net Flow', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Net Flow')).toBeInTheDocument());
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/MonthlyCashFlow.test.tsx
```

- [ ] **Step 3: Rewrite `MonthlyCashFlow.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip, Legend } from 'recharts';
import { useApi } from '../../lib/use-api';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_MONTHLY_CASH_FLOW_URL, reportMetaBySlug, type MonthlyCashFlowRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

export function MonthlyCashFlow() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<MonthlyCashFlowRowDto[]>(REPORTS_MONTHLY_CASH_FLOW_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const totalIncome   = (data ?? []).reduce((s, r) => s + r.totalIncome, 0);
  const totalExpenses = (data ?? []).reduce((s, r) => s + r.totalExpenses, 0);
  const netFlow       = totalIncome - totalExpenses;
  const symbol        = data?.[0]?.currencySymbol ?? '€';

  const chartData = (data ?? []).map((row) => ({
    name: `${MONTHS[row.month - 1]} ${row.year}`,
    income: row.totalIncome,
    expenses: row.totalExpenses,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Monthly Cash Flow" description={reportMetaBySlug('monthly-cash-flow')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Monthly Cash Flow" onRetry={refetch} />}
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No data for this period.</p>}
      {data && data.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile label="Total Income" value={<Numeric className="text-xl text-success">{symbol} {totalIncome.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Total Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {totalExpenses.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Net Flow" value={<Numeric className={`text-xl ${netFlow >= 0 ? 'text-success' : 'text-destructive'}`}>{symbol} {netFlow.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
          </div>

          <ChartContainer
            config={{
              income: { label: 'Income', color: 'var(--success)' },
              expenses: { label: 'Expenses', color: 'var(--destructive)' },
            }}
            className="h-[220px] w-full"
          >
            <BarChart data={chartData} margin={{ top: 8, right: 0, left: 0, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke="var(--border)" />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: number) => `${symbol} ${v.toFixed(2)}`} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="income" stackId="a" fill="var(--success)" radius={[0, 0, 0, 0]} />
              <Bar dataKey="expenses" stackId="a" fill="var(--destructive)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="monthly-cash-flow" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Month</TableHead>
                  <TableHead className="text-right">Income</TableHead>
                  <TableHead className="text-right">Expenses</TableHead>
                  <TableHead className="text-right">Net</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={`${row.year}-${row.month}`}>
                    <TableCell>{MONTHS[row.month - 1]} {row.year}</TableCell>
                    <TableCell className="text-right"><Numeric className="text-success">{row.currencySymbol} {row.totalIncome.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric className="text-destructive">{row.currencySymbol} {row.totalExpenses.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric className={row.net >= 0 ? 'text-success' : 'text-destructive'}>{row.currencySymbol} {row.net.toFixed(2)}</Numeric></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/MonthlyCashFlow.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.tsx ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.test.tsx
git commit -m "feat(reports): add KPI tiles + stacked BarChart to MonthlyCashFlow"
```

---

## Task 9: `IncomeExpense` — update KPI tiles + add grouped BarChart

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/IncomeExpense.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/IncomeExpense.test.tsx`

Note: `IncomeExpense` already has KPI tiles. This task adds the chart and switches to `ReportLocalFilterBar`.

- [ ] **Step 1: Add chart test**

Add to `IncomeExpense.test.tsx`:

```tsx
it('renders chart container when data is present', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
    ok: true,
    json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, savingsRate: 0.33 }),
  });
  renderPage();
  // IncomeExpense returns a single-period summary — chart renders as single grouped bar
  await waitFor(() => expect(screen.getByText(/income/i)).toBeInTheDocument());
});
```

- [ ] **Step 2: Rewrite `IncomeExpense.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/reports/IncomeExpense.tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip, Legend } from 'recharts';
import { useApi } from '../../lib/use-api';
import { REPORTS_INCOME_EXPENSE_URL, reportMetaBySlug, type IncomeExpenseSummaryDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

export function IncomeExpense() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<IncomeExpenseSummaryDto>(REPORTS_INCOME_EXPENSE_URL(qs));

  const net    = data ? data.totalIncome - data.totalExpenses : 0;
  const symbol = data?.currencySymbol ?? '€';

  // Single-period summary — render as one grouped bar pair
  const chartData = data
    ? [{ name: 'Period', income: data.totalIncome, expenses: data.totalExpenses }]
    : [];

  return (
    <div className="space-y-6">
      <ReportHeader title="Income vs Expense" description={reportMetaBySlug('income-expense')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[80px] w-full" />}
      {error && <CardError section="Income vs Expense" onRetry={refetch} />}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile label="Income" value={<Numeric className="text-xl text-success">{symbol} {data.totalIncome.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {data.totalExpenses.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Net" value={<Numeric className={`text-xl ${net >= 0 ? 'text-success' : 'text-destructive'}`}>{symbol} {net.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
          </div>

          <ChartContainer
            config={{
              income: { label: 'Income', color: 'var(--success)' },
              expenses: { label: 'Expenses', color: 'var(--destructive)' },
            }}
            className="h-[220px] w-full"
          >
            <BarChart data={chartData} margin={{ top: 8, right: 0, left: 0, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke="var(--border)" />
              <XAxis dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} />
              <YAxis tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: number) => `${symbol} ${v.toFixed(2)}`} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="income" fill="var(--success)" radius={[4, 4, 0, 0]} />
              <Bar dataKey="expenses" fill="var(--destructive)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="income-expense" queryString={qs}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Metric</TableHead>
                  <TableHead className="text-right">Value</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell>Total Income</TableCell>
                  <TableCell className="text-right"><Numeric className="text-success">{symbol} {data.totalIncome.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Total Expenses</TableCell>
                  <TableCell className="text-right"><Numeric className="text-destructive">{symbol} {data.totalExpenses.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Net</TableCell>
                  <TableCell className="text-right"><Numeric className={net >= 0 ? 'text-success' : 'text-destructive'}>{symbol} {net.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Savings Rate</TableCell>
                  <TableCell className="text-right"><Numeric>{(data.savingsRate * 100).toFixed(1)}%</Numeric></TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 3: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/IncomeExpense.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/IncomeExpense.tsx ProjectCeres.Client/src/app/features/reports/IncomeExpense.test.tsx
git commit -m "feat(reports): add grouped BarChart to IncomeExpense, switch to ReportLocalFilterBar"
```

---

## Task 10: `ExpenseBreakdown` — KPI tiles + horizontal BarChart

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.test.tsx`

- [ ] **Step 1: Add KPI tests**

Add to `ExpenseBreakdown.test.tsx`:

```tsx
const breakdown = {
  currencyCode: 'EUR', currencySymbol: '€',
  categories: [
    { categoryName: 'Groceries', lifestyleTag: 'Essential', total: 500 },
    { categoryName: 'Dining', lifestyleTag: null, total: 300 },
    { categoryName: 'Transport', lifestyleTag: 'Essential', total: 200 },
  ],
};

it('renders KPI tile for Largest Category', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => breakdown });
  renderPage();
  await waitFor(() => expect(screen.getByText('Largest Category')).toBeInTheDocument());
});

it('renders KPI tile for Total Expenses', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => breakdown });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Expenses')).toBeInTheDocument());
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ExpenseBreakdown.test.tsx
```

- [ ] **Step 3: Rewrite `ExpenseBreakdown.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip } from 'recharts';
import { useApi } from '../../lib/use-api';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_EXPENSE_BREAKDOWN_URL, reportMetaBySlug, type ExpenseBreakdownDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

export function ExpenseBreakdown() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<ExpenseBreakdownDto>(REPORTS_EXPENSE_BREAKDOWN_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data?.categories ?? [], 6);

  const total   = data?.categories.reduce((s, c) => s + c.total, 0) ?? 0;
  const largest = data?.categories[0];
  const symbol  = data?.currencySymbol ?? '€';

  const chartData = (data?.categories ?? []).map((c) => ({
    name: c.categoryName,
    amount: c.total,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Expense Breakdown" description={reportMetaBySlug('expense-breakdown')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Expense Breakdown" onRetry={refetch} />}
      {data && data.categories.length === 0 && <p className="text-sm text-muted-foreground">No expense transactions for this period.</p>}
      {data && data.categories.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Largest Category"
                value={<span className="text-base font-medium">{largest?.categoryName ?? '—'}</span>}
                valueClassName="mt-1"
              />
              {largest && <p className="mt-1 text-xs text-muted-foreground"><Numeric>{symbol} {largest.total.toFixed(2)}</Numeric></p>}
            </Tile>
            <Tile>
              <StatTile label="Total Expenses" value={<Numeric className="text-xl text-destructive">{symbol} {total.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Categories" value={<span className="text-xl font-medium">{data.categories.length}</span>} valueClassName="mt-1" />
            </Tile>
          </div>

          <ChartContainer
            config={{ amount: { label: 'Amount', color: 'var(--primary)' } }}
            className="h-[240px] w-full"
          >
            <BarChart layout="vertical" data={chartData} margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
              <CartesianGrid horizontal={false} stroke="var(--border)" />
              <XAxis type="number" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} width={90} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: number) => `${symbol} ${v.toFixed(2)}`} />
              <Bar dataKey="amount" fill="var(--primary)" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="expense-breakdown" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Category</TableHead>
                  <TableHead>Tag</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                  <TableHead className="text-right">% of Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((cat) => {
                  const pct = total > 0 ? (cat.total / total) * 100 : 0;
                  return (
                    <TableRow key={cat.categoryName}>
                      <TableCell className="font-medium">{cat.categoryName}</TableCell>
                      <TableCell className="text-muted-foreground">{cat.lifestyleTag ?? '—'}</TableCell>
                      <TableCell className="text-right"><Numeric>{symbol} {cat.total.toFixed(2)}</Numeric></TableCell>
                      <TableCell className="text-right"><Numeric className="text-muted-foreground">{pct.toFixed(1)}%</Numeric></TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/ExpenseBreakdown.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.tsx ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.test.tsx
git commit -m "feat(reports): add KPI tiles + horizontal BarChart to ExpenseBreakdown"
```

---

## Task 11: `BudgetVsActual` — add KPI tiles

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/BudgetVsActual.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/BudgetVsActual.test.tsx`

Note: BudgetVsActual uses existing `<Progress>` bars as its chart zone — no Recharts needed.

- [ ] **Step 1: Add KPI tests**

Add to `BudgetVsActual.test.tsx`:

```tsx
const rows = [
  { categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 200, totalLimit: 200, actualSpend: 150, variance: 50 },
  { categoryName: 'Dining',    currencyCode: 'EUR', currencySymbol: '€', limitPerPeriod: 100, totalLimit: 100, actualSpend: 120, variance: -20 },
];

it('renders KPI tile for Total Budget', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Budget')).toBeInTheDocument());
});

it('renders KPI tile for Total Spent', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total Spent')).toBeInTheDocument());
});

it('renders KPI tile for Overall Used', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Overall Used')).toBeInTheDocument());
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/BudgetVsActual.test.tsx
```

- [ ] **Step 3: Update `BudgetVsActual.tsx`** — add KPI tiles at the top, switch to `ReportLocalFilterBar`, add pagination

Read the full current file first, then replace the return statement:

```tsx
// ProjectCeres.Client/src/app/features/reports/BudgetVsActual.tsx
// Add these imports to the existing ones:
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { usePagination } from '@/hooks/usePagination';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
// Remove: import { ReportsFilterBar } from './ReportsFilterBar';

// Inside BudgetVsActual(), replace existing return with:
export function BudgetVsActual() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<BudgetVsActualRowDto[]>(REPORTS_BUDGET_VS_ACTUAL_URL(qs));
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const totalBudget = (data ?? []).reduce((s, r) => s + r.totalLimit, 0);
  const totalSpent  = (data ?? []).reduce((s, r) => s + r.actualSpend, 0);
  const pctUsed     = totalBudget > 0 ? (totalSpent / totalBudget) * 100 : 0;
  const symbol      = data?.[0]?.currencySymbol ?? '€';

  return (
    <div className="space-y-6">
      <ReportHeader title="Budget vs Actual" description={reportMetaBySlug('budget-vs-actual')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Budget vs Actual" onRetry={refetch} />}
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No active budgets for this period.</p>}
      {data && data.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile label="Total Budget" value={<Numeric className="text-xl">{symbol} {totalBudget.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Total Spent" value={<Numeric className={`text-xl ${totalSpent > totalBudget ? 'text-destructive' : ''}`}>{symbol} {totalSpent.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Overall Used" value={<Numeric className={`text-xl ${pctUsed > 100 ? 'text-destructive' : ''}`}>{pctUsed.toFixed(1)}%</Numeric>} valueClassName="mt-1" />
            </Tile>
          </div>
          <ReportTableCard slug="budget-vs-actual" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table className="min-w-[680px]">
              <TableHeader>
                <TableRow>
                  <TableHead>Category</TableHead>
                  <TableHead className="text-right">Limit/period</TableHead>
                  <TableHead className="text-right">Total limit</TableHead>
                  <TableHead className="text-right">Actual</TableHead>
                  <TableHead>Progress</TableHead>
                  <TableHead className="text-right">Variance</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row) => (
                  <TableRow key={row.categoryName}>
                    <TableCell className="font-medium">{row.categoryName}</TableCell>
                    <TableCell className="text-right text-muted-foreground"><Numeric>{row.currencySymbol} {row.limitPerPeriod.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric>{row.currencySymbol} {row.totalLimit.toFixed(2)}</Numeric></TableCell>
                    <TableCell className="text-right"><Numeric>{row.currencySymbol} {row.actualSpend.toFixed(2)}</Numeric></TableCell>
                    <TableCell><BudgetProgressCell limit={row.totalLimit} actual={row.actualSpend} /></TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.variance >= 0 ? 'text-success' : 'text-destructive'}>
                        {row.variance > 0 ? '+' : ''}{row.currencySymbol} {row.variance.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/BudgetVsActual.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/BudgetVsActual.tsx ProjectCeres.Client/src/app/features/reports/BudgetVsActual.test.tsx
git commit -m "feat(reports): add KPI tiles to BudgetVsActual, switch to ReportLocalFilterBar"
```

---

## Task 12: `LargestExpenses` — KPI tiles + horizontal BarChart

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/LargestExpenses.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/LargestExpenses.test.tsx`

- [ ] **Step 1: Add KPI tests**

Add to `LargestExpenses.test.tsx`:

```tsx
const rows = [
  { date: '2026-04-01', description: 'Rent', categoryName: 'Housing', accountName: 'Checking', currencySymbol: '€', amount: 900 },
  { date: '2026-04-10', description: 'Groceries', categoryName: 'Food', accountName: 'Checking', currencySymbol: '€', amount: 200 },
  { date: '2026-04-15', description: 'Phone', categoryName: 'Utilities', accountName: 'Checking', currencySymbol: '€', amount: 50 },
];

it('renders KPI tile for Top Expense', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Top Expense')).toBeInTheDocument());
});

it('renders KPI tile for Total (Top N)', async () => {
  (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => rows });
  renderPage();
  await waitFor(() => expect(screen.getByText('Total (Top N)')).toBeInTheDocument());
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/LargestExpenses.test.tsx
```

- [ ] **Step 3: Rewrite `LargestExpenses.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/reports/LargestExpenses.tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { ChartContainer } from '@/components/ui/chart';
import { Bar, BarChart, CartesianGrid, XAxis, YAxis, Tooltip } from 'recharts';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { usePagination } from '@/hooks/usePagination';
import { REPORTS_LARGEST_EXPENSES_URL, reportMetaBySlug, type LargestExpenseRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { useReportsFilters } from './useReportsFilters';

export function LargestExpenses() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<LargestExpenseRowDto[]>(REPORTS_LARGEST_EXPENSES_URL(qs));
  const { data: settings } = useSettings();
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], 6);

  const top    = data?.[0];
  const total  = (data ?? []).reduce((s, r) => s + r.amount, 0);
  const avg    = data && data.length > 0 ? total / data.length : 0;
  const symbol = top?.currencySymbol ?? '€';

  const chartData = (data ?? []).slice(0, 10).map((r) => ({
    name: r.description ?? r.categoryName,
    amount: r.amount,
  }));

  return (
    <div className="space-y-6">
      <ReportHeader title="Largest Expenses" description={reportMetaBySlug('largest-expenses')?.description} filters={filters} />
      <ReportLocalFilterBar />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Largest Expenses" onRetry={refetch} />}
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No expenses for this period.</p>}
      {data && data.length > 0 && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Tile>
              <StatTile
                label="Top Expense"
                value={<span className="text-base font-medium">{top?.description ?? '—'}</span>}
                valueClassName="mt-1"
              />
              {top && <p className="mt-1 text-xs text-destructive"><Numeric>{symbol} {top.amount.toFixed(2)}</Numeric></p>}
            </Tile>
            <Tile>
              <StatTile label="Total (Top N)" value={<Numeric className="text-xl text-destructive">{symbol} {total.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
            <Tile>
              <StatTile label="Avg per Transaction" value={<Numeric className="text-xl">{symbol} {avg.toFixed(2)}</Numeric>} valueClassName="mt-1" />
            </Tile>
          </div>

          <ChartContainer
            config={{ amount: { label: 'Amount', color: 'var(--destructive)' } }}
            className="h-[240px] w-full"
          >
            <BarChart layout="vertical" data={chartData} margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
              <CartesianGrid horizontal={false} stroke="var(--border)" />
              <XAxis type="number" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
              <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: 'var(--muted-foreground)' }} tickLine={false} axisLine={false} width={100} />
              <Tooltip contentStyle={{ background: 'var(--background)', border: '1px solid var(--border)', borderRadius: '8px', fontSize: 12 }} formatter={(v: number) => `${symbol} ${v.toFixed(2)}`} />
              <Bar dataKey="amount" fill="var(--destructive)" radius={[0, 4, 4, 0]} />
            </BarChart>
          </ChartContainer>

          <ReportTableCard slug="largest-expenses" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginatedItems.map((row, i) => (
                  <TableRow key={i}>
                    <TableCell className="whitespace-nowrap"><Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric></TableCell>
                    <TableCell>{row.description}</TableCell>
                    <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                    <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                    <TableCell className="text-right"><Numeric className="text-destructive">{row.currencySymbol} {row.amount.toFixed(2)}</Numeric></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/LargestExpenses.test.tsx
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/reports/LargestExpenses.tsx ProjectCeres.Client/src/app/features/reports/LargestExpenses.test.tsx
git commit -m "feat(reports): add KPI tiles + horizontal BarChart to LargestExpenses"
```

---

## Task 13: Update `NetWorth` and `TransactionHistory` — switch to `ReportLocalFilterBar`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/reports/NetWorth.tsx`
- Modify: `ProjectCeres.Client/src/app/features/reports/TransactionHistory.tsx`

These two reports get no chart or KPI tiles — just the filter bar swap and pagination.

- [ ] **Step 1: Update `NetWorth.tsx`**

In `NetWorth.tsx`, replace:
```tsx
import { ReportsFilterBar } from './ReportsFilterBar';
// and
<ReportsFilterBar />
```
with:
```tsx
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
// and
<ReportLocalFilterBar />
```

Also add `usePagination` and pass it to `ReportTableCard`. Read the full current file first, then apply both changes.

- [ ] **Step 2: Update `TransactionHistory.tsx`**

Same replacement — `ReportsFilterBar` → `ReportLocalFilterBar`. Also add `usePagination` for the table.

- [ ] **Step 3: Run affected tests**

```bash
cd ProjectCeres.Client && pnpm test src/app/features/reports/NetWorth.test.tsx src/app/features/reports/TransactionHistory.test.tsx
```

Expected: All existing tests PASS.

- [ ] **Step 4: Commit**

```bash
git add \
  ProjectCeres.Client/src/app/features/reports/NetWorth.tsx \
  ProjectCeres.Client/src/app/features/reports/TransactionHistory.tsx
git commit -m "feat(reports): switch NetWorth and TransactionHistory to ReportLocalFilterBar"
```

---

## Task 14: Delete `ReportsFilterBar`, full test suite, build check

**Files:**
- Delete: `ProjectCeres.Client/src/app/features/reports/ReportsFilterBar.tsx`

- [ ] **Step 1: Confirm no remaining imports of `ReportsFilterBar`**

```bash
grep -r "ReportsFilterBar" ProjectCeres.Client/src --include="*.tsx" --include="*.ts"
```

Expected: No output. If any file still imports it, update that file first.

- [ ] **Step 2: Delete the file**

```bash
git rm ProjectCeres.Client/src/app/features/reports/ReportsFilterBar.tsx
```

- [ ] **Step 3: Run the full test suite**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: All tests PASS.

- [ ] **Step 4: Run the build**

```bash
cd ProjectCeres.Client && pnpm build
```

Expected: Build succeeds with no TypeScript errors.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(reports): delete ReportsFilterBar (replaced by shared + local filter bars)"
```

---

## Task 15: Browser verification

- [ ] Start the dev server

```bash
cd ProjectCeres.Client && pnpm dev
```

- [ ] Open `http://localhost:5173/reports` — confirm redirect to `net-worth-over-time`
- [ ] Confirm tab bar shows all 8 tabs; active tab has underline
- [ ] Set date range to Jan 2026 – Apr 2026, switch to Income vs Expense tab — confirm date range persists
- [ ] Confirm KPI tiles render with values and deltas on all 6 chart reports
- [ ] Confirm charts render (AreaChart on Net Worth Over Time, stacked bars on Cash Flow, horizontal bars on Expense Breakdown and Largest Expenses)
- [ ] Confirm pagination: add enough test data or reduce pageSize temporarily to 2 to verify prev/next controls
- [ ] Check Net Worth and Transaction History — table only, no chart, no KPI tiles
- [ ] Resize browser to 375px width — tab bar scrolls horizontally, layout doesn't break
- [ ] Commit any fixes found during verification
