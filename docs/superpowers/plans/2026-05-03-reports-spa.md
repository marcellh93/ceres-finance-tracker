# Reports SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace 9 Razor Reports views with a React SPA at `/app/reports/*`, reusing the existing `ReportsApiController` endpoints (all 8 already ship with `?format=csv`).

**Architecture:** Hybrid layout — `<ReportsLayout>` owns a sticky filter bar and renders `<Outlet>`; index card-grid at `/app/reports`; each of 8 reports at `/app/reports/<slug>`. Filter state lives in URL searchParams via `useReportsFilters`. Universal recipe: KPI strip → shadcn Table → Export CSV link. Budget vs Actual adds an inline `<Progress>` cell. No new chart families.

**Tech Stack:** React 19, React Router v6, TypeScript, Tailwind v4, shadcn/ui base-nova, Lucide icons, Vitest + React Testing Library, Sonner toasts.

---

## File map

**New — shared:**
- `src/components/DateRangePicker.tsx` — generic date-range picker extracted from MovementsDateRangePicker

**New — feature:**
- `src/app/features/reports/reports-api.ts` — URL constants + all DTO types
- `src/app/features/reports/useReportsFilters.ts` — URL searchParam hook
- `src/app/features/reports/csv-export.ts` — CSV href builder
- `src/app/features/reports/ReportsLayout.tsx` — Outlet wrapper + filter bar
- `src/app/features/reports/ReportsFilterBar.tsx` — sticky filter bar
- `src/app/features/reports/ReportsIndex.tsx` — 8-card index grid
- `src/app/features/reports/ReportHeader.tsx` — shared ← back / h2 / period summary
- `src/app/features/reports/ReportTableCard.tsx` — shared Card + Export CSV + Table slot
- `src/app/features/reports/NetWorth.tsx`
- `src/app/features/reports/NetWorthOverTime.tsx`
- `src/app/features/reports/IncomeExpense.tsx`
- `src/app/features/reports/MonthlyCashFlow.tsx`
- `src/app/features/reports/ExpenseBreakdown.tsx`
- `src/app/features/reports/BudgetVsActual.tsx`
- `src/app/features/reports/LargestExpenses.tsx`
- `src/app/features/reports/TransactionHistory.tsx`
- Colocated `*.test.tsx` for every file above

**Modified:**
- `src/app/App.tsx` — replace single `<Route path="reports">` with nested layout + 8 child routes
- `src/app/pages/Reports.tsx` — delete (file removed)
- `src/app/features/movements/MovementsDateRangePicker.tsx` — thin wrapper around new `DateRangePicker`
- `ProjectCeres/Controllers/ReportsController.cs` — all actions → 302 redirects
- `ProjectCeres/Views/Reports/` — delete all 9 `.cshtml` files
- `docs/api-contract.md` — update Reports section
- `docs/planning-phase3-spa-migration.md` — mark Reports migrated

---

## Task 1: Extract generic DateRangePicker

**Files:**
- Create: `ProjectCeres.Client/src/components/DateRangePicker.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsDateRangePicker.tsx`
- Test: `ProjectCeres.Client/src/components/DateRangePicker.test.tsx`

- [ ] **Step 1.1: Write the failing test for DateRangePicker**

Create `ProjectCeres.Client/src/components/DateRangePicker.test.tsx`:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DateRangePicker } from './DateRangePicker';

vi.mock('@/components/ui/calendar', () => ({
  Calendar: ({ selected, onSelect }: { selected: unknown; onSelect: (r: unknown) => void }) => (
    <div data-testid="mock-calendar">
      <button
        type="button"
        onClick={() => onSelect({ from: new Date(2026, 2, 5), to: new Date(2026, 2, 12) })}
      >
        pick-range
      </button>
    </div>
  ),
}));

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(new Date(2026, 4, 1, 12, 0, 0));
});
afterEach(() => { vi.useRealTimers(); vi.resetAllMocks(); });

function LocationSpy({ onChange }: { onChange: (s: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderPicker(initial = '/test', fromKey = 'from', toKey = 'to') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/test"
          element={
            <>
              <DateRangePicker fromKey={fromKey} toKey={toKey} />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('DateRangePicker', () => {
  it('renders "Any date" when no params', () => {
    renderPicker();
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent(/any date/i);
  });

  it('shows formatted range when both params are set', () => {
    renderPicker('/test?from=2026-04-01&to=2026-04-30');
    expect(screen.getByRole('button', { name: /date range/i })).toHaveTextContent('01/04/2026');
  });

  it('This month preset writes correct from/to and clears page', async () => {
    const get = renderPicker('/test?page=3');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'This month' }));
    await waitFor(() => {
      const s = get();
      expect(s).toContain('from=2026-05-01');
      expect(s).toContain('to=2026-05-31');
      expect(s).not.toContain('page=');
    });
  });

  it('custom fromKey/toKey writes to different param names', async () => {
    const get = renderPicker('/test', 'dateFrom', 'dateTo');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'This month' }));
    await waitFor(() => {
      expect(get()).toContain('dateFrom=2026-05-01');
      expect(get()).toContain('dateTo=2026-05-31');
    });
  });

  it('Clear removes both params', async () => {
    const get = renderPicker('/test?from=2026-04-01&to=2026-04-30');
    fireEvent.click(screen.getByRole('button', { name: /date range/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'Clear' }));
    await waitFor(() => {
      expect(get()).not.toContain('from=');
    });
  });
});
```

- [ ] **Step 1.2: Run test — expect FAIL (module not found)**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/components/DateRangePicker.test.tsx
```

Expected: fail with "Cannot find module './DateRangePicker'"

- [ ] **Step 1.3: Create DateRangePicker**

Create `ProjectCeres.Client/src/components/DateRangePicker.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import type { DateRange } from 'react-day-picker';
import { CalendarRange } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Calendar } from '@/components/ui/calendar';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useSettings } from '../app/lib/use-settings';
import { formatDate } from '../app/lib/date-format';

// ---------- helpers (exported for tests) ----------

export function toIsoDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function parseIsoDate(value: string | null): Date | undefined {
  if (!value) return undefined;
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return undefined;
  const d = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(d.getTime()) ? undefined : d;
}

export function readDraftFromParams(
  params: URLSearchParams,
  fromKey: string,
  toKey: string,
): DateRange | undefined {
  const from = parseIsoDate(params.get(fromKey));
  const to = parseIsoDate(params.get(toKey));
  if (!from && !to) return undefined;
  return { from, to };
}

export function formatRangeLabel(
  range: DateRange | undefined,
  dateFormat: string | undefined,
): string {
  if (!range || (!range.from && !range.to)) return 'Any date';
  if (range.from && range.to)
    return `${formatDate(toIsoDate(range.from), dateFormat)} – ${formatDate(toIsoDate(range.to), dateFormat)}`;
  if (range.from) return `From ${formatDate(toIsoDate(range.from), dateFormat)}`;
  return `Until ${formatDate(toIsoDate(range.to as Date), dateFormat)}`;
}

// ---------- preset builders ----------

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

function addDays(d: Date, days: number): Date {
  const next = new Date(d);
  next.setDate(next.getDate() + days);
  return next;
}

type Preset = { label: string; build: () => DateRange | undefined };

export const DATE_RANGE_PRESETS: Preset[] = [
  { label: 'Today', build: () => { const t = startOfDay(new Date()); return { from: t, to: t }; } },
  { label: 'Yesterday', build: () => { const y = addDays(startOfDay(new Date()), -1); return { from: y, to: y }; } },
  {
    label: 'This week',
    build: () => {
      const today = startOfDay(new Date());
      const from = addDays(today, -((today.getDay() + 6) % 7));
      return { from, to: addDays(from, 6) };
    },
  },
  {
    label: 'Last week',
    build: () => {
      const today = startOfDay(new Date());
      const thisMon = addDays(today, -((today.getDay() + 6) % 7));
      const from = addDays(thisMon, -7);
      return { from, to: addDays(from, 6) };
    },
  },
  {
    label: 'This month',
    build: () => {
      const now = new Date();
      return { from: new Date(now.getFullYear(), now.getMonth(), 1), to: new Date(now.getFullYear(), now.getMonth() + 1, 0) };
    },
  },
  {
    label: 'Last month',
    build: () => {
      const now = new Date();
      return { from: new Date(now.getFullYear(), now.getMonth() - 1, 1), to: new Date(now.getFullYear(), now.getMonth(), 0) };
    },
  },
  {
    label: 'This quarter',
    build: () => {
      const now = new Date();
      const qStart = Math.floor(now.getMonth() / 3) * 3;
      return { from: new Date(now.getFullYear(), qStart, 1), to: new Date(now.getFullYear(), qStart + 3, 0) };
    },
  },
  {
    label: 'Year to date',
    build: () => { const now = startOfDay(new Date()); return { from: new Date(now.getFullYear(), 0, 1), to: now }; },
  },
  {
    label: 'Last 12 months',
    build: () => {
      const to = startOfDay(new Date());
      const from = new Date(to);
      from.setFullYear(from.getFullYear() - 1);
      from.setDate(from.getDate() + 1);
      return { from, to };
    },
  },
  { label: 'All time', build: () => undefined },
];

// ---------- component ----------

type Props = {
  /** URL param key for the start date. Default: 'from' */
  fromKey?: string;
  /** URL param key for the end date. Default: 'to' */
  toKey?: string;
};

export function DateRangePicker({ fromKey = 'from', toKey = 'to' }: Props) {
  const [params, setParams] = useSearchParams();
  const { data: settings } = useSettings();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<DateRange | undefined>(() =>
    readDraftFromParams(params, fromKey, toKey),
  );

  const fromParam = params.get(fromKey);
  const toParam = params.get(toKey);
  useEffect(() => {
    setDraft(readDraftFromParams(params, fromKey, toKey));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fromParam, toParam]);

  const currentRange = readDraftFromParams(params, fromKey, toKey);
  const triggerLabel = formatRangeLabel(currentRange, settings?.dateFormat);

  function applyRange(range: DateRange | undefined) {
    const next = new URLSearchParams(params);
    if (range?.from) next.set(fromKey, toIsoDate(range.from));
    else next.delete(fromKey);
    if (range?.to) next.set(toKey, toIsoDate(range.to));
    else next.delete(toKey);
    next.delete('page');
    setParams(next, { replace: true });
  }

  function handlePreset(preset: Preset) {
    const range = preset.build();
    setDraft(range);
    applyRange(range);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            aria-label="Date range"
            className="min-w-[14rem] justify-start font-normal"
          >
            <CalendarRange className="mr-2 size-4 opacity-70" aria-hidden="true" />
            <span className="truncate">{triggerLabel}</span>
          </Button>
        }
      />
      <PopoverContent align="start" className="w-auto max-w-[calc(100vw-2rem)] p-0">
        <div className="flex flex-col sm:flex-row">
          <div className="flex flex-row flex-wrap gap-1 border-b p-2 sm:flex-col sm:flex-nowrap sm:border-r sm:border-b-0">
            {DATE_RANGE_PRESETS.map((p) => (
              <Button key={p.label} type="button" variant="ghost" size="sm" className="justify-start" onClick={() => handlePreset(p)}>
                {p.label}
              </Button>
            ))}
          </div>
          <div className="flex flex-col">
            <Calendar
              mode="range"
              numberOfMonths={typeof window !== 'undefined' && window.innerWidth < 640 ? 1 : 2}
              weekStartsOn={1}
              selected={draft}
              onSelect={setDraft}
              defaultMonth={draft?.from ?? new Date()}
            />
            <div className="flex justify-end gap-2 border-t p-2">
              <Button type="button" variant="ghost" size="sm" onClick={() => { setDraft(undefined); applyRange(undefined); setOpen(false); }}>
                Clear
              </Button>
              <Button type="button" size="sm" onClick={() => { applyRange(draft); setOpen(false); }}>
                Apply
              </Button>
            </div>
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 1.4: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/components/DateRangePicker.test.tsx
```

Expected: all 5 pass.

- [ ] **Step 1.5: Refactor MovementsDateRangePicker as thin wrapper**

Replace `ProjectCeres.Client/src/app/features/movements/MovementsDateRangePicker.tsx` entirely:

```tsx
// Re-export helpers that tests import directly.
export { toIsoDate, readDraftFromParams as readDraftFromParamsBase, formatRangeLabel } from '@/components/DateRangePicker';
import { readDraftFromParams as baseFn } from '@/components/DateRangePicker';
import { DateRangePicker } from '@/components/DateRangePicker';
import { useSearchParams } from 'react-router-dom';

// Keep the named export that tests use for readDraftFromParams(params)
export function readDraftFromParams(params: URLSearchParams) {
  return baseFn(params, 'from', 'to');
}

export function MovementsDateRangePicker() {
  return <DateRangePicker fromKey="from" toKey="to" />;
}
```

- [ ] **Step 1.6: Run existing MovementsDateRangePicker tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/movements/MovementsDateRangePicker.test.tsx
```

Expected: all 9 pass.

- [ ] **Step 1.7: Run full test suite — expect no regressions**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: all pass.

- [ ] **Step 1.8: Commit**

```bash
cd ProjectCeres.Client && git add src/components/DateRangePicker.tsx src/components/DateRangePicker.test.tsx src/app/features/movements/MovementsDateRangePicker.tsx
git commit -m "refactor(date-range-picker): extract generic DateRangePicker; Movements wraps it"
```

---

## Task 2: reports-api.ts + useReportsFilters + csv-export

**Files:**
- Create: `ProjectCeres.Client/src/app/features/reports/reports-api.ts`
- Create: `ProjectCeres.Client/src/app/features/reports/useReportsFilters.ts`
- Create: `ProjectCeres.Client/src/app/features/reports/csv-export.ts`
- Test: `ProjectCeres.Client/src/app/features/reports/useReportsFilters.test.ts`

- [ ] **Step 2.1: Write the failing test**

Create `ProjectCeres.Client/src/app/features/reports/useReportsFilters.test.ts`:

```ts
import { renderHook, act } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { createElement } from 'react';
import { useReportsFilters } from './useReportsFilters';

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(new Date(2026, 4, 1, 12, 0, 0)); // 2026-05-01
});
afterEach(() => { vi.useRealTimers(); });

function wrapper({ children }: { children: React.ReactNode }) {
  return createElement(MemoryRouter, { initialEntries: ['/reports'] }, children);
}

describe('useReportsFilters', () => {
  it('defaults from/to to current month when absent', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    expect(result.current.filters.from).toBe('2026-05-01');
    expect(result.current.filters.to).toBe('2026-05-31');
  });

  it('setFilter updates a single key', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 2));
    expect(result.current.filters.currencyId).toBe(2);
  });

  it('toQueryString encodes present filters only', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 3));
    const qs = result.current.toQueryString();
    expect(qs).toContain('currencyId=3');
    expect(qs).toContain('from=2026-05-01');
    expect(qs).not.toContain('accountId');
  });

  it('reset restores defaults', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 99));
    act(() => result.current.reset());
    expect(result.current.filters.currencyId).toBeNull();
  });
});
```

- [ ] **Step 2.2: Run test — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/useReportsFilters.test.ts
```

Expected: fail with "Cannot find module"

- [ ] **Step 2.3: Create reports-api.ts**

Create `ProjectCeres.Client/src/app/features/reports/reports-api.ts`:

```ts
// URL constants
export const REPORTS_NET_WORTH_URL          = '/api/reports/net-worth';
export const REPORTS_INCOME_EXPENSE_URL     = (qs: string) => `/api/reports/income-expense?${qs}`;
export const REPORTS_EXPENSE_BREAKDOWN_URL  = (qs: string) => `/api/reports/expense-breakdown?${qs}`;
export const REPORTS_TRANSACTION_HISTORY_URL = (qs: string) => `/api/reports/transaction-history?${qs}`;
export const REPORTS_BUDGET_VS_ACTUAL_URL   = (qs: string) => `/api/reports/budget-vs-actual?${qs}`;
export const REPORTS_LARGEST_EXPENSES_URL   = (qs: string) => `/api/reports/largest-expenses?${qs}`;
export const REPORTS_MONTHLY_CASH_FLOW_URL  = (qs: string) => `/api/reports/monthly-cash-flow?${qs}`;
export const REPORTS_NET_WORTH_OVER_TIME_URL = (qs: string) => `/api/reports/net-worth-over-time?${qs}`;

// DTOs — shaped to match what ReportsApiController returns

export type NetWorthEntryDto = {
  currencyCode: string;
  currencySymbol: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};

export type IncomeExpenseSummaryDto = {
  currencyCode: string;
  currencySymbol: string;
  totalIncome: number;
  totalExpenses: number;
  savingsRate: number;
};

export type CategoryExpenseDto = {
  categoryName: string;
  lifestyleTag: string | null;
  total: number;
};

export type ExpenseBreakdownDto = {
  currencyCode: string;
  currencySymbol: string;
  categories: CategoryExpenseDto[];
};

export type TransactionHistoryRowDto = {
  id: string;
  date: string;
  accountName: string;
  categoryName: string;
  categoryTypeName: string;
  description: string | null;
  amount: number;
  currencySymbol: string;
};

export type BudgetVsActualRowDto = {
  categoryName: string;
  currencyCode: string;
  currencySymbol: string;
  limitAmount: number;
  actualSpend: number;
  variance: number;
};

export type LargestExpenseRowDto = {
  date: string;
  description: string;
  categoryName: string;
  accountName: string;
  currencySymbol: string;
  amount: number;
};

export type MonthlyCashFlowRowDto = {
  year: number;
  month: number;
  currencyCode: string;
  currencySymbol: string;
  totalIncome: number;
  totalExpenses: number;
  net: number;
};

export type NetWorthSnapshotRowDto = {
  year: number;
  month: number;
  currencyCode: string;
  currencySymbol: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};

// Slug → label map (used by ReportsIndex and ReportHeader)
export const REPORT_META: Array<{
  slug: string;
  label: string;
  description: string;
}> = [
  { slug: 'net-worth',          label: 'Net Worth',            description: 'Current assets, liabilities, and net worth across all accounts.' },
  { slug: 'net-worth-over-time', label: 'Net Worth Over Time',  description: 'How your net worth has evolved month by month.' },
  { slug: 'income-expense',     label: 'Income vs Expense',    description: 'Total income, expenses, and savings rate for the period.' },
  { slug: 'monthly-cash-flow',  label: 'Monthly Cash Flow',    description: 'Income and expenses broken down by calendar month.' },
  { slug: 'expense-breakdown',  label: 'Expense Breakdown',    description: 'Spending ranked by category for the period.' },
  { slug: 'budget-vs-actual',   label: 'Budget vs Actual',     description: 'How actual spending compares to your category budgets.' },
  { slug: 'largest-expenses',   label: 'Largest Expenses',     description: 'Your highest individual expenses, ranked.' },
  { slug: 'transaction-history', label: 'Transaction History', description: 'Paginated ledger of all transactions for the period.' },
];
```

- [ ] **Step 2.4: Create useReportsFilters.ts**

Create `ProjectCeres.Client/src/app/features/reports/useReportsFilters.ts`:

```ts
import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';

export type ReportsFilters = {
  from: string | null;
  to: string | null;
  currencyId: number | null;
  accountId: string | null;
  categoryId: string | null;
  limit: number | null;
  page: number | null;
};

function currentMonthRange(): { from: string; to: string } {
  const now = new Date();
  const y = now.getFullYear();
  const m = now.getMonth();
  const lastDay = new Date(y, m + 1, 0).getDate();
  const pad = (n: number) => String(n).padStart(2, '0');
  return { from: `${y}-${pad(m + 1)}-01`, to: `${y}-${pad(m + 1)}-${pad(lastDay)}` };
}

function read(params: URLSearchParams): ReportsFilters {
  const defaults = currentMonthRange();
  return {
    from: params.get('from') ?? defaults.from,
    to: params.get('to') ?? defaults.to,
    currencyId: params.has('currencyId') ? Number(params.get('currencyId')) : null,
    accountId: params.get('accountId'),
    categoryId: params.get('categoryId'),
    limit: params.has('limit') ? Number(params.get('limit')) : null,
    page: params.has('page') ? Number(params.get('page')) : null,
  };
}

export function useReportsFilters() {
  const [params, setParams] = useSearchParams();
  const filters = read(params);

  const setFilter = useCallback(
    <K extends keyof ReportsFilters>(key: K, value: ReportsFilters[K]) => {
      const next = new URLSearchParams(params);
      if (value === null || value === undefined) {
        next.delete(key);
      } else {
        next.set(key, String(value));
      }
      setParams(next, { replace: true });
    },
    [params, setParams],
  );

  const reset = useCallback(() => {
    const next = new URLSearchParams(params);
    ['currencyId', 'accountId', 'categoryId', 'limit', 'page'].forEach((k) => next.delete(k));
    setParams(next, { replace: true });
  }, [params, setParams]);

  const toQueryString = useCallback((): string => {
    const p = new URLSearchParams();
    if (filters.from) p.set('from', filters.from);
    if (filters.to) p.set('to', filters.to);
    if (filters.currencyId !== null) p.set('currencyId', String(filters.currencyId));
    if (filters.accountId) p.set('accountId', filters.accountId);
    if (filters.categoryId) p.set('categoryId', filters.categoryId);
    if (filters.limit !== null) p.set('limit', String(filters.limit));
    if (filters.page !== null) p.set('page', String(filters.page));
    return p.toString();
  }, [filters]);

  return { filters, setFilter, reset, toQueryString };
}
```

- [ ] **Step 2.5: Create csv-export.ts**

Create `ProjectCeres.Client/src/app/features/reports/csv-export.ts`:

```ts
export function buildCsvHref(slug: string, queryString: string): string {
  const qs = queryString ? `${queryString}&format=csv` : 'format=csv';
  return `/api/reports/${slug}?${qs}`;
}
```

- [ ] **Step 2.6: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/useReportsFilters.test.ts
```

Expected: all 4 pass.

- [ ] **Step 2.7: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/
git commit -m "feat(reports): add reports-api.ts, useReportsFilters, csv-export"
```

---

## Task 3: ReportsLayout, ReportsFilterBar, ReportsIndex + route wiring

**Files:**
- Create: `src/app/features/reports/ReportHeader.tsx`
- Create: `src/app/features/reports/ReportTableCard.tsx`
- Create: `src/app/features/reports/ReportsFilterBar.tsx`
- Create: `src/app/features/reports/ReportsLayout.tsx`
- Create: `src/app/features/reports/ReportsIndex.tsx`
- Create: colocated test files
- Modify: `src/app/App.tsx`
- Delete: `src/app/pages/Reports.tsx`

- [ ] **Step 3.1: Write failing tests**

Create `ProjectCeres.Client/src/app/features/reports/ReportsIndex.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { ReportsIndex } from './ReportsIndex';

beforeEach(() => { global.fetch = vi.fn().mockReturnValue(new Promise(() => {})); });
afterEach(() => { vi.resetAllMocks(); });

describe('ReportsIndex', () => {
  it('renders all 8 report cards', () => {
    render(
      <MemoryRouter initialEntries={['/reports']}>
        <Routes>
          <Route path="reports" element={<ReportsLayout />}>
            <Route index element={<ReportsIndex />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: 'Reports' })).toBeInTheDocument();
    expect(screen.getByText('Net Worth')).toBeInTheDocument();
    expect(screen.getByText('Net Worth Over Time')).toBeInTheDocument();
    expect(screen.getByText('Income vs Expense')).toBeInTheDocument();
    expect(screen.getByText('Monthly Cash Flow')).toBeInTheDocument();
    expect(screen.getByText('Expense Breakdown')).toBeInTheDocument();
    expect(screen.getByText('Budget vs Actual')).toBeInTheDocument();
    expect(screen.getByText('Largest Expenses')).toBeInTheDocument();
    expect(screen.getByText('Transaction History')).toBeInTheDocument();
  });
});
```

- [ ] **Step 3.2: Run test — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/ReportsIndex.test.tsx
```

Expected: fail with "Cannot find module"

- [ ] **Step 3.3: Create ReportHeader.tsx**

Create `ProjectCeres.Client/src/app/features/reports/ReportHeader.tsx`:

```tsx
import { useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import { ChevronLeft } from 'lucide-react';
import type { ReportsFilters } from './useReportsFilters';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  title: string;
  filters: ReportsFilters;
  showPeriod?: boolean;
};

export function ReportHeader({ title, filters, showPeriod = true }: Props) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const { data: settings } = useSettings();

  useEffect(() => { headingRef.current?.focus(); }, []);

  const periodSummary =
    showPeriod && filters.from && filters.to
      ? `${formatDate(filters.from, settings?.dateFormat)} – ${formatDate(filters.to, settings?.dateFormat)}`
      : null;

  return (
    <div className="space-y-1" style={{ viewTransitionName: `report-header-${title.toLowerCase().replace(/\s+/g, '-')}` }}>
      <Link
        to="/reports"
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        Reports
      </Link>
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-2xl font-semibold outline-none"
      >
        {title}
      </h1>
      {periodSummary && (
        <p className="text-sm text-muted-foreground">{periodSummary}</p>
      )}
    </div>
  );
}
```

- [ ] **Step 3.4: Create ReportTableCard.tsx**

Create `ProjectCeres.Client/src/app/features/reports/ReportTableCard.tsx`:

```tsx
import { Download } from 'lucide-react';
import { toast } from 'sonner';
import { buttonVariants } from '@/components/ui/button';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import { buildCsvHref } from './csv-export';

type Props = {
  slug: string;
  queryString: string;
  children: React.ReactNode;
};

export function ReportTableCard({ slug, queryString, children }: Props) {
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
          <Download className="h-4 w-4" />
          Export CSV
        </a>
      </CardHeader>
      <CardContent className="overflow-x-auto">
        {children}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3.5: Create ReportsFilterBar.tsx**

Create `ProjectCeres.Client/src/app/features/reports/ReportsFilterBar.tsx`:

```tsx
import { useMatch } from 'react-router-dom';
import { DateRangePicker } from '@/components/DateRangePicker';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { useReportsFilters } from './useReportsFilters';

// Reports that consume each filter
const USES_CURRENCY = new Set(['income-expense', 'expense-breakdown', 'transaction-history', 'budget-vs-actual', 'largest-expenses', 'monthly-cash-flow', 'net-worth-over-time']);
const USES_RANGE    = new Set(['income-expense', 'expense-breakdown', 'transaction-history', 'budget-vs-actual', 'largest-expenses', 'monthly-cash-flow', 'net-worth-over-time']);
const USES_ACCOUNT  = new Set(['transaction-history']);
const USES_CATEGORY = new Set(['expense-breakdown', 'transaction-history']);

function useCurrentSlug(): string | null {
  const match = useMatch('/reports/:slug');
  return match?.params.slug ?? null;
}

export function ReportsFilterBar() {
  const slug = useCurrentSlug();
  const { filters, setFilter } = useReportsFilters();

  const showCurrency = slug ? USES_CURRENCY.has(slug) : false;
  const showRange    = slug ? USES_RANGE.has(slug) : false;
  const showAccount  = slug ? USES_ACCOUNT.has(slug) : false;
  const showCategory = slug ? USES_CATEGORY.has(slug) : false;

  const { data: accounts } = useApi<AccountOptionDto[]>(showAccount ? ACCOUNTS_ACTIVE_URL : '');
  const { data: categories } = useApi<CategoryOptionDto[]>(showCategory ? CATEGORIES_ACTIVE_URL : '');

  if (!showRange && !showCurrency && !showAccount && !showCategory) return null;

  return (
    <div className="sticky top-14 z-10 -mx-6 border-b bg-background/95 px-6 py-3 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="flex flex-wrap items-center gap-2">
        {showRange && <DateRangePicker fromKey="from" toKey="to" />}
        {showCurrency && (
          <CurrencyCombobox
            value={filters.currencyId}
            onChange={(id) => setFilter('currencyId', id)}
            placeholder="Currency"
          />
        )}
        {showAccount && (
          <AccountCombobox
            accounts={accounts ?? []}
            value={filters.accountId}
            onChange={(id) => setFilter('accountId', id)}
            placeholder="All accounts"
          />
        )}
        {showCategory && (
          <CategoryCombobox
            categories={categories ?? []}
            value={filters.categoryId}
            onChange={(id) => setFilter('categoryId', id)}
            placeholder="All categories"
          />
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 3.6: Create ReportsLayout.tsx**

Create `ProjectCeres.Client/src/app/features/reports/ReportsLayout.tsx`:

```tsx
import { Outlet } from 'react-router-dom';
import { ReportsFilterBar } from './ReportsFilterBar';

export function ReportsLayout() {
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <ReportsFilterBar />
      <Outlet />
    </div>
  );
}
```

- [ ] **Step 3.7: Create ReportsIndex.tsx**

Create `ProjectCeres.Client/src/app/features/reports/ReportsIndex.tsx`:

```tsx
import { useEffect, useRef } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { BarChart3 } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { REPORT_META } from './reports-api';

export function ReportsIndex() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [params] = useSearchParams();
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="space-y-6">
      <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
        Reports
      </h1>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {REPORT_META.map(({ slug, label, description }) => (
          <Link
            key={slug}
            to={`/reports/${slug}?${params.toString()}`}
            className="group no-underline"
            style={{ viewTransitionName: `report-card-${slug}` }}
          >
            <Card className="h-full transition-colors group-hover:border-primary/50 group-hover:bg-accent/30">
              <CardContent className="flex h-full flex-col gap-3 pt-6">
                <BarChart3 className="h-6 w-6 text-primary" aria-hidden="true" />
                <div>
                  <p className="font-medium">{label}</p>
                  <p className="mt-1 text-sm text-muted-foreground">{description}</p>
                </div>
              </CardContent>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 3.8: Wire routes in App.tsx**

In `ProjectCeres.Client/src/app/App.tsx`, add these imports at the top (alongside existing feature imports):

```tsx
import { ReportsLayout } from './features/reports/ReportsLayout';
import { ReportsIndex } from './features/reports/ReportsIndex';
// Report pages — added as stubs first; replaced in Tasks 4-7
import { NetWorth } from './features/reports/NetWorth';
import { NetWorthOverTime } from './features/reports/NetWorthOverTime';
import { IncomeExpense } from './features/reports/IncomeExpense';
import { MonthlyCashFlow } from './features/reports/MonthlyCashFlow';
import { ExpenseBreakdown } from './features/reports/ExpenseBreakdown';
import { BudgetVsActual } from './features/reports/BudgetVsActual';
import { LargestExpenses } from './features/reports/LargestExpenses';
import { TransactionHistory } from './features/reports/TransactionHistory';
```

Replace:
```tsx
<Route path="reports" element={<Reports />} />
```
with:
```tsx
<Route path="reports" element={<ReportsLayout />}>
  <Route index element={<ReportsIndex />} />
  <Route path="net-worth" element={<NetWorth />} />
  <Route path="net-worth-over-time" element={<NetWorthOverTime />} />
  <Route path="income-expense" element={<IncomeExpense />} />
  <Route path="monthly-cash-flow" element={<MonthlyCashFlow />} />
  <Route path="expense-breakdown" element={<ExpenseBreakdown />} />
  <Route path="budget-vs-actual" element={<BudgetVsActual />} />
  <Route path="largest-expenses" element={<LargestExpenses />} />
  <Route path="transaction-history" element={<TransactionHistory />} />
</Route>
```

Remove the `import { Reports } from './pages/Reports';` line.

Also create stub files for the 8 report pages so the import resolves. Each stub is the same shape — repeat for all 8 (`NetWorth.tsx`, `NetWorthOverTime.tsx`, `IncomeExpense.tsx`, `MonthlyCashFlow.tsx`, `ExpenseBreakdown.tsx`, `BudgetVsActual.tsx`, `LargestExpenses.tsx`, `TransactionHistory.tsx`):

```tsx
// Example: src/app/features/reports/NetWorth.tsx
import { PagePlaceholder } from '../../components/PagePlaceholder';
export function NetWorth() {
  return <PagePlaceholder title="Net Worth" description="Coming soon." />;
}
```

(Use the correct export name for each file: `NetWorthOverTime`, `IncomeExpense`, etc.)

- [ ] **Step 3.9: Delete old Reports page**

```bash
rm ProjectCeres.Client/src/app/pages/Reports.tsx
```

- [ ] **Step 3.10: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/ReportsIndex.test.tsx
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/App.test.tsx
```

Expected: ReportsIndex test passes. App.test.tsx `/reports` test passes (heading "Reports" still renders from ReportsIndex).

- [ ] **Step 3.11: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/ src/app/App.tsx
git rm src/app/pages/Reports.tsx
git commit -m "feat(reports): layout, filter bar, index card-grid, route wiring"
```

---

## Task 4: Net Worth + Net Worth Over Time report pages

**Files:**
- Modify: `src/app/features/reports/NetWorth.tsx`
- Modify: `src/app/features/reports/NetWorthOverTime.tsx`
- Create: `src/app/features/reports/NetWorth.test.tsx`
- Create: `src/app/features/reports/NetWorthOverTime.test.tsx`

- [ ] **Step 4.1: Write failing tests**

Create `ProjectCeres.Client/src/app/features/reports/NetWorth.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { NetWorth } from './NetWorth';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/net-worth']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="net-worth" element={<NetWorth />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('NetWorth report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Net Worth' })).toBeInTheDocument();
  });

  it('renders skeleton while loading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(document.querySelector('[data-slot="skeleton"]')).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no accounts/i)).toBeInTheDocument());
  });

  it('renders currency rows when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ currencyCode: 'EUR', currencySymbol: '€', assets: 1000, liabilities: 200, netWorth: 800 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('EUR')).toBeInTheDocument());
    expect(screen.getByText(/800/)).toBeInTheDocument();
  });

  it('renders CardError on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('fail'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/Couldn't load Net Worth/)).toBeInTheDocument());
  });
});
```

Create `ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { NetWorthOverTime } from './NetWorthOverTime';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/net-worth-over-time']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="net-worth-over-time" element={<NetWorthOverTime />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('NetWorthOverTime report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Net Worth Over Time' })).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no data/i)).toBeInTheDocument());
  });

  it('renders Export CSV link', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByRole('link', { name: /export csv/i })).toBeInTheDocument());
  });

  it('renders month rows when data is present', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', assets: 5000, liabilities: 1000, netWorth: 4000 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Apr 2026')).toBeInTheDocument());
  });
});
```

- [ ] **Step 4.2: Run tests — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/NetWorth.test.tsx src/app/features/reports/NetWorthOverTime.test.tsx
```

Expected: fail (stubs render PagePlaceholder, not real content)

- [ ] **Step 4.3: Implement NetWorth.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/NetWorth.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_NET_WORTH_URL, type NetWorthEntryDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function NetWorth() {
  const { filters, toQueryString } = useReportsFilters();
  const { data, error, loading, refetch } = useApi<NetWorthEntryDto[]>(REPORTS_NET_WORTH_URL);

  return (
    <div className="space-y-6">
      <ReportHeader title="Net Worth" filters={filters} showPeriod={false} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No accounts found.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="net-worth" queryString={toQueryString()}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Currency</TableHead>
                <TableHead className="text-right">Assets</TableHead>
                <TableHead className="text-right">Liabilities</TableHead>
                <TableHead className="text-right">Net Worth</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((row) => (
                <TableRow key={row.currencyCode}>
                  <TableCell>{row.currencyCode}</TableCell>
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
      )}
    </div>
  );
}
```

- [ ] **Step 4.4: Implement NetWorthOverTime.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/NetWorthOverTime.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_NET_WORTH_OVER_TIME_URL, type NetWorthSnapshotRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

function monthLabel(row: NetWorthSnapshotRowDto): string {
  return `${MONTHS[row.month - 1]} ${row.year}`;
}

export function NetWorthOverTime() {
  const { filters, toQueryString } = useReportsFilters();
  const url = REPORTS_NET_WORTH_OVER_TIME_URL(toQueryString());
  const { data, error, loading, refetch } = useApi<NetWorthSnapshotRowDto[]>(url);

  return (
    <div className="space-y-6">
      <ReportHeader title="Net Worth Over Time" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No data for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="net-worth-over-time" queryString={toQueryString()}>
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
              {data.map((row) => (
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
      )}
    </div>
  );
}
```

- [ ] **Step 4.5: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/NetWorth.test.tsx src/app/features/reports/NetWorthOverTime.test.tsx
```

Expected: all pass.

- [ ] **Step 4.6: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/NetWorth.tsx src/app/features/reports/NetWorth.test.tsx src/app/features/reports/NetWorthOverTime.tsx src/app/features/reports/NetWorthOverTime.test.tsx
git commit -m "feat(reports): Net Worth and Net Worth Over Time pages"
```

---

## Task 5: Income vs Expense + Monthly Cash Flow

**Files:**
- Modify: `src/app/features/reports/IncomeExpense.tsx`
- Modify: `src/app/features/reports/MonthlyCashFlow.tsx`
- Create: test files

- [ ] **Step 5.1: Write failing tests**

Create `ProjectCeres.Client/src/app/features/reports/IncomeExpense.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { IncomeExpense } from './IncomeExpense';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/income-expense']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="income-expense" element={<IncomeExpense />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('IncomeExpense report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Income vs Expense' })).toBeInTheDocument();
  });

  it('renders KPI values when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, savingsRate: 0.333 }),
    });
    renderPage();
    await waitFor(() => expect(screen.getByText(/3000/)).toBeInTheDocument());
    expect(screen.getByText(/2000/)).toBeInTheDocument();
    expect(screen.getByText(/33.3%/)).toBeInTheDocument();
  });

  it('renders CardError on failure', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('fail'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/Couldn't load Income vs Expense/)).toBeInTheDocument());
  });
});
```

Create `ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { MonthlyCashFlow } from './MonthlyCashFlow';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/monthly-cash-flow']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="monthly-cash-flow" element={<MonthlyCashFlow />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('MonthlyCashFlow report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Monthly Cash Flow' })).toBeInTheDocument();
  });

  it('renders month rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ year: 2026, month: 4, currencyCode: 'EUR', currencySymbol: '€', totalIncome: 3000, totalExpenses: 2000, net: 1000 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Apr 2026')).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no data/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 5.2: Run tests — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/IncomeExpense.test.tsx src/app/features/reports/MonthlyCashFlow.test.tsx
```

- [ ] **Step 5.3: Implement IncomeExpense.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/IncomeExpense.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Tile } from '@/components/Tile';
import { StatTile } from '@/components/StatTile';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_INCOME_EXPENSE_URL, type IncomeExpenseSummaryDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function IncomeExpense() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<IncomeExpenseSummaryDto>(REPORTS_INCOME_EXPENSE_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Income vs Expense" filters={filters} />
      {loading && <Skeleton className="h-[80px] w-full" />}
      {error && <CardError section="Income vs Expense" onRetry={refetch} />}
      {data && (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-3">
            <Tile>
              <StatTile
                label="Income"
                value={<Numeric className="text-xl text-success">{data.currencySymbol} {data.totalIncome.toFixed(2)}</Numeric>}
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={<Numeric className="text-xl text-destructive">{data.currencySymbol} {data.totalExpenses.toFixed(2)}</Numeric>}
              />
            </Tile>
            <Tile>
              <StatTile
                label="Savings Rate"
                value={<Numeric className="text-xl">{(data.savingsRate * 100).toFixed(1)}%</Numeric>}
              />
            </Tile>
          </div>
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
                  <TableCell className="text-right"><Numeric className="text-success">{data.currencySymbol} {data.totalIncome.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Total Expenses</TableCell>
                  <TableCell className="text-right"><Numeric className="text-destructive">{data.currencySymbol} {data.totalExpenses.toFixed(2)}</Numeric></TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>Net</TableCell>
                  <TableCell className="text-right">
                    <Numeric className={(data.totalIncome - data.totalExpenses) >= 0 ? 'text-success' : 'text-destructive'}>
                      {data.currencySymbol} {(data.totalIncome - data.totalExpenses).toFixed(2)}
                    </Numeric>
                  </TableCell>
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

- [ ] **Step 5.4: Implement MonthlyCashFlow.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/MonthlyCashFlow.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_MONTHLY_CASH_FLOW_URL, type MonthlyCashFlowRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

export function MonthlyCashFlow() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<MonthlyCashFlowRowDto[]>(REPORTS_MONTHLY_CASH_FLOW_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Monthly Cash Flow" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Monthly Cash Flow" onRetry={refetch} />}
      {data && data.length === 0 && <p className="text-sm text-muted-foreground">No data for this period.</p>}
      {data && data.length > 0 && (
        <ReportTableCard slug="monthly-cash-flow" queryString={qs}>
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
              {data.map((row) => (
                <TableRow key={`${row.year}-${row.month}`}>
                  <TableCell>{MONTHS[row.month - 1]} {row.year}</TableCell>
                  <TableCell className="text-right"><Numeric className="text-success">{row.currencySymbol} {row.totalIncome.toFixed(2)}</Numeric></TableCell>
                  <TableCell className="text-right"><Numeric className="text-destructive">{row.currencySymbol} {row.totalExpenses.toFixed(2)}</Numeric></TableCell>
                  <TableCell className="text-right">
                    <Numeric className={row.net >= 0 ? 'text-success' : 'text-destructive'}>
                      {row.currencySymbol} {row.net.toFixed(2)}
                    </Numeric>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
```

- [ ] **Step 5.5: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/IncomeExpense.test.tsx src/app/features/reports/MonthlyCashFlow.test.tsx
```

- [ ] **Step 5.6: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/IncomeExpense.tsx src/app/features/reports/IncomeExpense.test.tsx src/app/features/reports/MonthlyCashFlow.tsx src/app/features/reports/MonthlyCashFlow.test.tsx
git commit -m "feat(reports): Income vs Expense and Monthly Cash Flow pages"
```

---

## Task 6: Expense Breakdown + Budget vs Actual

**Files:**
- Modify: `src/app/features/reports/ExpenseBreakdown.tsx`
- Modify: `src/app/features/reports/BudgetVsActual.tsx`
- Create: test files

- [ ] **Step 6.1: Write failing tests**

Create `ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { ExpenseBreakdown } from './ExpenseBreakdown';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/expense-breakdown']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="expense-breakdown" element={<ExpenseBreakdown />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('ExpenseBreakdown report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Expense Breakdown' })).toBeInTheDocument();
  });

  it('renders category rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', categories: [{ categoryName: 'Groceries', lifestyleTag: null, total: 300 }] }),
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Groceries')).toBeInTheDocument());
  });

  it('renders empty state when categories is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', categories: [] }) });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no expense/i)).toBeInTheDocument());
  });
});
```

Create `ProjectCeres.Client/src/app/features/reports/BudgetVsActual.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { BudgetVsActual } from './BudgetVsActual';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/budget-vs-actual']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="budget-vs-actual" element={<BudgetVsActual />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('BudgetVsActual report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Budget vs Actual' })).toBeInTheDocument();
  });

  it('renders rows with progress when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitAmount: 400, actualSpend: 312, variance: -88 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Groceries')).toBeInTheDocument());
    expect(document.querySelector('[data-slot="progress"]')).toBeInTheDocument();
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no budgets/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 6.2: Run tests — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/ExpenseBreakdown.test.tsx src/app/features/reports/BudgetVsActual.test.tsx
```

- [ ] **Step 6.3: Implement ExpenseBreakdown.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/ExpenseBreakdown.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_EXPENSE_BREAKDOWN_URL, type ExpenseBreakdownDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function ExpenseBreakdown() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<ExpenseBreakdownDto>(REPORTS_EXPENSE_BREAKDOWN_URL(qs));

  const total = data?.categories.reduce((sum, c) => sum + c.total, 0) ?? 0;

  return (
    <div className="space-y-6">
      <ReportHeader title="Expense Breakdown" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Expense Breakdown" onRetry={refetch} />}
      {data && data.categories.length === 0 && (
        <p className="text-sm text-muted-foreground">No expense transactions for this period.</p>
      )}
      {data && data.categories.length > 0 && (
        <ReportTableCard slug="expense-breakdown" queryString={qs}>
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
              {data.categories.map((cat) => {
                const pct = total > 0 ? (cat.total / total) * 100 : 0;
                return (
                  <TableRow key={cat.categoryName}>
                    <TableCell className="font-medium">{cat.categoryName}</TableCell>
                    <TableCell className="text-muted-foreground">{cat.lifestyleTag ?? '—'}</TableCell>
                    <TableCell className="text-right">
                      <Numeric>{data.currencySymbol} {cat.total.toFixed(2)}</Numeric>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className="text-muted-foreground">{pct.toFixed(1)}%</Numeric>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
```

- [ ] **Step 6.4: Implement BudgetVsActual.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/BudgetVsActual.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Progress, ProgressIndicator, ProgressTrack } from '@/components/ui/progress';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { cn } from '@/lib/utils';
import { REPORTS_BUDGET_VS_ACTUAL_URL, type BudgetVsActualRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

function BudgetProgressCell({ limit, actual }: { limit: number; actual: number }) {
  const pct = limit === 0 ? 0 : Math.min((actual / limit) * 100, 100);
  const isOver = actual > limit;
  return (
    <div className="flex items-center gap-3">
      <Progress
        value={pct}
        aria-label={`${pct.toFixed(0)}% of budget`}
        className="w-[140px]"
      >
        <ProgressTrack>
          <ProgressIndicator
            className={cn('h-full transition-all', isOver ? 'bg-destructive' : 'bg-primary')}
          />
        </ProgressTrack>
      </Progress>
      <Numeric className="text-xs text-muted-foreground w-10 text-right">{pct.toFixed(0)}%</Numeric>
    </div>
  );
}

export function BudgetVsActual() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<BudgetVsActualRowDto[]>(REPORTS_BUDGET_VS_ACTUAL_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Budget vs Actual" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Budget vs Actual" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No active budgets for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="budget-vs-actual" queryString={qs}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Category</TableHead>
                <TableHead className="text-right">Limit</TableHead>
                <TableHead className="text-right">Actual</TableHead>
                <TableHead>Progress</TableHead>
                <TableHead className="text-right">Variance</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((row) => (
                <TableRow key={row.categoryName}>
                  <TableCell className="font-medium">{row.categoryName}</TableCell>
                  <TableCell className="text-right">
                    <Numeric>{row.currencySymbol} {row.limitAmount.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric>{row.currencySymbol} {row.actualSpend.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell>
                    <BudgetProgressCell limit={row.limitAmount} actual={row.actualSpend} />
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric className={row.variance <= 0 ? 'text-success' : 'text-destructive'}>
                      {row.variance > 0 ? '+' : ''}{row.currencySymbol} {row.variance.toFixed(2)}
                    </Numeric>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
```

- [ ] **Step 6.5: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/ExpenseBreakdown.test.tsx src/app/features/reports/BudgetVsActual.test.tsx
```

- [ ] **Step 6.6: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/ExpenseBreakdown.tsx src/app/features/reports/ExpenseBreakdown.test.tsx src/app/features/reports/BudgetVsActual.tsx src/app/features/reports/BudgetVsActual.test.tsx
git commit -m "feat(reports): Expense Breakdown and Budget vs Actual pages"
```

---

## Task 7: Largest Expenses + Transaction History

**Files:**
- Modify: `src/app/features/reports/LargestExpenses.tsx`
- Modify: `src/app/features/reports/TransactionHistory.tsx`
- Create: test files

- [ ] **Step 7.1: Write failing tests**

Create `ProjectCeres.Client/src/app/features/reports/LargestExpenses.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { LargestExpenses } from './LargestExpenses';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/largest-expenses']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="largest-expenses" element={<LargestExpenses />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('LargestExpenses report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Largest Expenses' })).toBeInTheDocument();
  });

  it('renders rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ date: '2026-05-01', description: 'IKEA', categoryName: 'Shopping', accountName: 'Checking', currencySymbol: '€', amount: 350 }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('IKEA')).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no expenses/i)).toBeInTheDocument());
  });
});
```

Create `ProjectCeres.Client/src/app/features/reports/TransactionHistory.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ReportsLayout } from './ReportsLayout';
import { TransactionHistory } from './TransactionHistory';

beforeEach(() => { global.fetch = vi.fn(); });
afterEach(() => { vi.resetAllMocks(); });

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/reports/transaction-history']}>
      <Routes>
        <Route path="reports" element={<ReportsLayout />}>
          <Route path="transaction-history" element={<TransactionHistory />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('TransactionHistory report', () => {
  it('renders heading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByRole('heading', { name: 'Transaction History' })).toBeInTheDocument();
  });

  it('renders rows when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => [{ id: '1', date: '2026-05-01', accountName: 'Checking', categoryName: 'Groceries', categoryTypeName: 'Expense', description: 'Lidl', amount: 45, currencySymbol: '€' }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Lidl')).toBeInTheDocument());
  });

  it('renders pagination controls when data loads', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => Array.from({ length: 50 }, (_, i) => ({ id: String(i), date: '2026-05-01', accountName: 'Checking', categoryName: 'Groceries', categoryTypeName: 'Expense', description: `Tx ${i}`, amount: 10, currencySymbol: '€' })),
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('button', { name: /next/i })).toBeInTheDocument());
  });

  it('renders empty state when data is []', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no transactions/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 7.2: Run tests — expect FAIL**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/LargestExpenses.test.tsx src/app/features/reports/TransactionHistory.test.tsx
```

- [ ] **Step 7.3: Implement LargestExpenses.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/LargestExpenses.tsx`:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { REPORTS_LARGEST_EXPENSES_URL, type LargestExpenseRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

export function LargestExpenses() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<LargestExpenseRowDto[]>(REPORTS_LARGEST_EXPENSES_URL(qs));
  const { data: settings } = useSettings();

  return (
    <div className="space-y-6">
      <ReportHeader title="Largest Expenses" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Largest Expenses" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No expenses for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="largest-expenses" queryString={qs}>
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
              {data.map((row, i) => (
                <TableRow key={i}>
                  <TableCell className="whitespace-nowrap">
                    <Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric>
                  </TableCell>
                  <TableCell>{row.description}</TableCell>
                  <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                  <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                  <TableCell className="text-right">
                    <Numeric className="text-destructive">{row.currencySymbol} {row.amount.toFixed(2)}</Numeric>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
```

- [ ] **Step 7.4: Implement TransactionHistory.tsx**

Replace `ProjectCeres.Client/src/app/features/reports/TransactionHistory.tsx`:

```tsx
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Badge } from '@/components/ui/badge';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { REPORTS_TRANSACTION_HISTORY_URL, type TransactionHistoryRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const PAGE_SIZE = 50;

export function TransactionHistory() {
  const { filters, setFilter, toQueryString } = useReportsFilters();
  const page = filters.page ?? 1;
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<TransactionHistoryRowDto[]>(REPORTS_TRANSACTION_HISTORY_URL(qs));
  const { data: settings } = useSettings();

  const hasNext = (data?.length ?? 0) === PAGE_SIZE;
  const hasPrev = page > 1;

  return (
    <div className="space-y-6">
      <ReportHeader title="Transaction History" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Transaction History" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No transactions for this period.</p>
      )}
      {data && data.length > 0 && (
        <>
          <ReportTableCard slug="transaction-history" queryString={qs}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell className="whitespace-nowrap">
                      <Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric>
                    </TableCell>
                    <TableCell>{row.description ?? '—'}</TableCell>
                    <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                    <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                    <TableCell>
                      <Badge variant={row.categoryTypeName === 'Income' ? 'success' : 'secondary'}>
                        {row.categoryTypeName}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.categoryTypeName === 'Income' ? 'text-success' : 'text-destructive'}>
                        {row.currencySymbol} {row.amount.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
          <div className="flex items-center justify-between">
            <p className="text-sm text-muted-foreground">Page {page}</p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={!hasPrev}
                onClick={() => setFilter('page', page - 1)}
              >
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!hasNext}
                onClick={() => setFilter('page', page + 1)}
                aria-label="Next page"
              >
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 7.5: Run tests — expect PASS**

```bash
cd ProjectCeres.Client && pnpm test --reporter=verbose src/app/features/reports/LargestExpenses.test.tsx src/app/features/reports/TransactionHistory.test.tsx
```

- [ ] **Step 7.6: Run full client test suite**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: all pass.

- [ ] **Step 7.7: Type-check**

```bash
cd ProjectCeres.Client && pnpm build
```

Expected: exits 0.

- [ ] **Step 7.8: Commit**

```bash
cd ProjectCeres.Client && git add src/app/features/reports/LargestExpenses.tsx src/app/features/reports/LargestExpenses.test.tsx src/app/features/reports/TransactionHistory.tsx src/app/features/reports/TransactionHistory.test.tsx
git commit -m "feat(reports): Largest Expenses and Transaction History pages"
```

---

## Task 8: Razor cleanup — redirects + delete views

**Files:**
- Modify: `ProjectCeres/Controllers/ReportsController.cs`
- Delete: `ProjectCeres/Views/Reports/*.cshtml` (9 files)

- [ ] **Step 8.1: Replace all controller actions with 302 redirects**

Replace the entire content of `ProjectCeres/Controllers/ReportsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class ReportsController : Controller
{
    [HttpGet] public IActionResult Index()              => Redirect("/app/reports");
    [HttpGet] public IActionResult NetWorth()           => Redirect("/app/reports/net-worth");
    [HttpGet] public IActionResult IncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/income-expense", currencyId, from, to));
    [HttpGet] public IActionResult ExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/expense-breakdown", currencyId, from, to));
    [HttpGet] public IActionResult TransactionHistory(int? currencyId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId, int page = 1)
        => Redirect(BuildRedirect("/app/reports/transaction-history", currencyId, from, to, accountId, categoryId, page));
    [HttpGet] public IActionResult BudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/budget-vs-actual", currencyId, from, to));
    [HttpGet] public IActionResult LargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
        => Redirect(BuildRedirect("/app/reports/largest-expenses", currencyId, from, to, limit: limit));
    [HttpGet] public IActionResult MonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/monthly-cash-flow", currencyId, from, to));
    [HttpGet] public IActionResult NetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/net-worth-over-time", currencyId, from, to));

    // Export actions redirect to the matching SPA report page; the user can
    // re-download from the Export CSV button there.
    [HttpGet] public IActionResult ExportNetWorth()           => Redirect("/app/reports/net-worth");
    [HttpGet] public IActionResult ExportIncomeExpense(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/income-expense", currencyId, from, to));
    [HttpGet] public IActionResult ExportExpenseBreakdown(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/expense-breakdown", currencyId, from, to));
    [HttpGet] public IActionResult ExportTransactionHistory(int? currencyId, DateOnly? from, DateOnly? to, Guid? accountId, Guid? categoryId)
        => Redirect(BuildRedirect("/app/reports/transaction-history", currencyId, from, to, accountId, categoryId));
    [HttpGet] public IActionResult ExportBudgetVsActual(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/budget-vs-actual", currencyId, from, to));
    [HttpGet] public IActionResult ExportLargestExpenses(int? currencyId, DateOnly? from, DateOnly? to, int limit = 25)
        => Redirect(BuildRedirect("/app/reports/largest-expenses", currencyId, from, to, limit: limit));
    [HttpGet] public IActionResult ExportMonthlyCashFlow(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/monthly-cash-flow", currencyId, from, to));
    [HttpGet] public IActionResult ExportNetWorthOverTime(int? currencyId, DateOnly? from, DateOnly? to)
        => Redirect(BuildRedirect("/app/reports/net-worth-over-time", currencyId, from, to));

    private static string BuildRedirect(string basePath, int? currencyId = null, DateOnly? from = null, DateOnly? to = null, Guid? accountId = null, Guid? categoryId = null, int? page = null, int? limit = null)
    {
        var qs = new System.Text.StringBuilder();
        void Append(string key, string value)
        {
            qs.Append(qs.Length == 0 ? '?' : '&');
            qs.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }
        if (currencyId.HasValue) Append("currencyId", currencyId.Value.ToString());
        if (from.HasValue)       Append("from", from.Value.ToString("yyyy-MM-dd"));
        if (to.HasValue)         Append("to",   to.Value.ToString("yyyy-MM-dd"));
        if (accountId.HasValue)  Append("accountId", accountId.Value.ToString());
        if (categoryId.HasValue) Append("categoryId", categoryId.Value.ToString());
        if (page is > 1)         Append("page", page.Value.ToString());
        if (limit.HasValue)      Append("limit", limit.Value.ToString());
        return basePath + qs;
    }
}
```

- [ ] **Step 8.2: Delete Razor view files**

```bash
rm ProjectCeres/Views/Reports/Index.cshtml \
   ProjectCeres/Views/Reports/NetWorth.cshtml \
   ProjectCeres/Views/Reports/IncomeExpense.cshtml \
   ProjectCeres/Views/Reports/ExpenseBreakdown.cshtml \
   ProjectCeres/Views/Reports/TransactionHistory.cshtml \
   ProjectCeres/Views/Reports/BudgetVsActual.cshtml \
   ProjectCeres/Views/Reports/LargestExpenses.cshtml \
   ProjectCeres/Views/Reports/MonthlyCashFlow.cshtml \
   ProjectCeres/Views/Reports/NetWorthOverTime.cshtml
```

- [ ] **Step 8.3: Fix stale link in navbar.tsx**

In `ProjectCeres.Client/src/components/ui/navbar.tsx` line 48, find the stale `<a href="/Reports">` and remove or update it. If the element is unused remove it; if it's a nav link update to `/reports`.

```bash
grep -n "Reports" ProjectCeres.Client/src/components/ui/navbar.tsx
```

Edit that line to remove or point to `/reports`.

- [ ] **Step 8.4: Build server — expect no errors**

```bash
dotnet build ProjectCeres
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 8.5: Run all server tests**

```bash
dotnet test
```

Expected: all pass (24 ReportsApiTests + 14 ReportGeneratorTests + 12 ReportServiceTests + 8 ReportGeneratorFactoryTests unchanged).

- [ ] **Step 8.6: Commit**

```bash
git add ProjectCeres/Controllers/ReportsController.cs ProjectCeres.Client/src/components/ui/navbar.tsx
git rm ProjectCeres/Views/Reports/Index.cshtml ProjectCeres/Views/Reports/NetWorth.cshtml ProjectCeres/Views/Reports/IncomeExpense.cshtml ProjectCeres/Views/Reports/ExpenseBreakdown.cshtml ProjectCeres/Views/Reports/TransactionHistory.cshtml ProjectCeres/Views/Reports/BudgetVsActual.cshtml ProjectCeres/Views/Reports/LargestExpenses.cshtml ProjectCeres/Views/Reports/MonthlyCashFlow.cshtml ProjectCeres/Views/Reports/NetWorthOverTime.cshtml
git commit -m "feat(reports): replace Razor views with 302 redirects; delete cshtml files"
```

---

## Task 9: Doc sync + web-design-guidelines audit

**Files:**
- Modify: `docs/api-contract.md`
- Modify: `docs/planning-phase3-spa-migration.md`

- [ ] **Step 9.1: Run web-design-guidelines audit**

Invoke skill `web-design-guidelines` against the changed frontend files:

```
src/app/features/reports/ReportsLayout.tsx
src/app/features/reports/ReportsFilterBar.tsx
src/app/features/reports/ReportsIndex.tsx
src/app/features/reports/ReportHeader.tsx
src/app/features/reports/ReportTableCard.tsx
src/app/features/reports/NetWorth.tsx
src/app/features/reports/NetWorthOverTime.tsx
src/app/features/reports/IncomeExpense.tsx
src/app/features/reports/MonthlyCashFlow.tsx
src/app/features/reports/ExpenseBreakdown.tsx
src/app/features/reports/BudgetVsActual.tsx
src/app/features/reports/LargestExpenses.tsx
src/app/features/reports/TransactionHistory.tsx
src/components/DateRangePicker.tsx
```

Fix any issues inline before proceeding.

- [ ] **Step 9.2: Update api-contract.md Reports section**

Find the Reports row (near line 393) and replace with accurate content reflecting all 8 endpoints and the `?format=csv` convention. The current text only lists 4 reports. Correct it to:

```
| Reports | GET only (generated on demand) | 8 reports: net-worth, income-expense, expense-breakdown, transaction-history, budget-vs-actual, largest-expenses, monthly-cash-flow, net-worth-over-time. All accept `?format=csv` for download. |
| SavedReports | Deferred (ADR-0055) | Entity exists; CRUD not built. |
```

- [ ] **Step 9.3: Update planning-phase3-spa-migration.md**

In the Batch 2 table (§8), update Reports row:

```markdown
| 5 | Reports | ✅ Migrated 2026-05-03 | 8 report pages + ReportsLayout + sticky filter bar + CSV export. DateRangePicker extracted as shared component. Razor views deleted; ReportsController actions → 302 redirects. |
```

In §2 (controller-by-controller map), update the ReportsController row status to `**Migrated (2026-05-03).**`

- [ ] **Step 9.4: Run sync-docs skill**

Invoke the `sync-docs` skill against the git diff to ensure all documentation stays in sync.

- [ ] **Step 9.5: Final full test run**

```bash
cd ProjectCeres.Client && pnpm test && pnpm build
dotnet test
```

Expected: all pass, build exits 0.

- [ ] **Step 9.6: Commit**

```bash
git add docs/api-contract.md docs/planning-phase3-spa-migration.md
git commit -m "docs(reports): sync api-contract and spa-migration docs after Reports SPA migration"
```
