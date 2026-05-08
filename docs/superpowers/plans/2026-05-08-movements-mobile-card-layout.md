# Movements Mobile Card Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the desktop `MovementsTable` with a stacked card layout below 768 px so mobile users see a touch-friendly transaction list with no horizontal overflow.

**Architecture:** Single new component `MovementsCardList.tsx` that mirrors `MovementsTable`'s `Props` contract. `MovementsLayout` reads `useMediaQuery('(min-width: 768px)')` and renders one or the other. Status toggle and ⋮ menu retain their own tap zones via `e.stopPropagation()` so card-body taps navigate to Edit while inner controls behave as today.

**Tech Stack:** React 19, react-router-dom (`<Link>`), Tailwind CSS v4, shadcn `<Card>` primitive, lucide-react icons, Vitest + React Testing Library.

**Spec reference:** `docs/superpowers/specs/2026-05-08-movements-mobile-card-layout-design.md` (commits `31e138e`, `5c2e047`).

---

## File structure

### Files created

| Path | Responsibility |
|---|---|
| `ProjectCeres.Client/src/app/features/movements/MovementsCardList.tsx` | Renders the card list. Same `Props` shape as `MovementsTable`. |
| `ProjectCeres.Client/src/app/features/movements/MovementsCardList.test.tsx` | Vitest unit tests covering rendering, type variants, tap-isolation, a11y. |

### Files modified

| Path | Why |
|---|---|
| `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx` | Add `useMediaQuery` and switch between `<MovementsTable>` and `<MovementsCardList>` at line ~186. |

`MovementsTable.tsx`, `MovementClearedToggle.tsx`, `MovementRowMenu.tsx`, `MovementsPagination.tsx`, `movement-type-display.ts`, `movements-api.ts` are **unchanged**.

---

## Task 1: Build MovementsCardList component (TDD)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsCardList.tsx`
- Create: `ProjectCeres.Client/src/app/features/movements/MovementsCardList.test.tsx`

### Step 1: Write the failing tests

- [ ] **Step 1: Create the test file**

Create `ProjectCeres.Client/src/app/features/movements/MovementsCardList.test.tsx`:

```tsx
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { MovementsCardList } from './MovementsCardList';
import type { MovementListItemDto } from './movements-api';

// Mock useSettings so the component can format dates and numbers without a real settings fetch.
vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({
    data: { numberFormat: 'period_decimal', dateFormat: 'yyyy-MM-dd' },
    error: undefined,
    loading: false,
    refetch: vi.fn(),
  }),
}));

// Mock fetch so MovementClearedToggle's PATCH doesn't blow up.
beforeEach(() => {
  globalThis.fetch = vi.fn(() => Promise.resolve(new Response(null, { status: 204 }))) as unknown as typeof fetch;
});

const transaction: MovementListItemDto = {
  id: 't1',
  movementType: 'Transaction',
  date: '2026-03-12',
  amount: 42.5,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: 'Spotify subscription',
  isCleared: true,
  isOpeningBalance: false,
  accountName: 'Checking Account (BBVA)',
  categoryName: 'Subscriptions',
  categoryTypeName: 'Expense',
  sourceAccountName: null,
  destAccountName: null,
  assetAccountName: null,
  liabilityAccountName: null,
};

const transfer: MovementListItemDto = {
  id: 'tr1',
  movementType: 'Transfer',
  date: '2026-03-13',
  amount: 100,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: null,
  isCleared: false,
  isOpeningBalance: false,
  accountName: null,
  categoryName: null,
  categoryTypeName: null,
  sourceAccountName: 'Cash',
  destAccountName: 'Savings',
  assetAccountName: null,
  liabilityAccountName: null,
};

const liabilityPayment: MovementListItemDto = {
  id: 'lp1',
  movementType: 'LiabilityPayment',
  date: '2026-03-14',
  amount: 250,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: null,
  isCleared: false,
  isOpeningBalance: false,
  accountName: null,
  categoryName: null,
  categoryTypeName: null,
  sourceAccountName: null,
  destAccountName: null,
  assetAccountName: 'Checking Account (BBVA)',
  liabilityAccountName: 'Credit Card (BBVA)',
};

const transactionNoDescription: MovementListItemDto = {
  ...transaction,
  id: 't2',
  description: null,
};

function renderList(items: MovementListItemDto[], onRefetch = vi.fn()) {
  return render(
    <MemoryRouter>
      <MovementsCardList items={items} onRefetch={onRefetch} />
    </MemoryRouter>,
  );
}

describe('MovementsCardList', () => {
  it('renders one article per item, preserving order', () => {
    renderList([transaction, transfer, liabilityPayment]);
    const articles = screen.getAllByRole('article');
    expect(articles).toHaveLength(3);
    expect(within(articles[0]).getByText('Spotify subscription')).toBeInTheDocument();
    expect(within(articles[1]).getByText('Cash → Savings')).toBeInTheDocument();
    expect(within(articles[2]).getByText('Checking Account (BBVA) → Credit Card (BBVA)')).toBeInTheDocument();
  });

  it('shows the description as primary text on a transaction card', () => {
    renderList([transaction]);
    expect(screen.getByText('Spotify subscription')).toBeInTheDocument();
    expect(screen.getByText('Checking Account (BBVA)')).toBeInTheDocument();
  });

  it('falls back to category name when transaction description is null', () => {
    renderList([transactionNoDescription]);
    expect(screen.getByText('Subscriptions')).toBeInTheDocument();
  });

  it('shows source → destination on a transfer card', () => {
    renderList([transfer]);
    expect(screen.getByText('Cash → Savings')).toBeInTheDocument();
  });

  it('shows asset → liability on a liability payment card', () => {
    renderList([liabilityPayment]);
    expect(screen.getByText('Checking Account (BBVA) → Credit Card (BBVA)')).toBeInTheDocument();
  });

  it('renders the type pill for each variant', () => {
    renderList([transaction, transfer, liabilityPayment]);
    expect(screen.getByText('Transaction')).toBeInTheDocument();
    expect(screen.getByText('Transfer')).toBeInTheDocument();
    expect(screen.getByText('Debt Payment')).toBeInTheDocument();
  });

  it('card link points to /movements/{id}/edit', () => {
    renderList([transaction]);
    const link = screen.getByRole('link');
    expect(link).toHaveAttribute('href', '/movements/t1/edit');
  });

  it('card link aria-label describes the movement', () => {
    renderList([transaction]);
    const link = screen.getByRole('link');
    expect(link).toHaveAttribute('aria-label', expect.stringContaining('Transaction'));
    expect(link).toHaveAttribute('aria-label', expect.stringContaining('2026-03-12'));
  });

  it('clicking the status toggle does NOT navigate (stopPropagation)', () => {
    renderList([transaction]);
    const toggle = screen.getByRole('button', { name: /mark as pending/i });
    const link = screen.getByRole('link');
    const linkClickSpy = vi.fn();
    link.addEventListener('click', linkClickSpy);
    fireEvent.click(toggle);
    expect(linkClickSpy).not.toHaveBeenCalled();
  });

  it('renders pending badge for an uncleared movement', () => {
    renderList([transfer]);
    expect(screen.getByText(/pending/i)).toBeInTheDocument();
  });

  it('list container exposes the list role implicitly', () => {
    renderList([transaction, transfer]);
    const list = screen.getByRole('list');
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
  });
});
```

### Step 2: Run the test to verify it fails

- [ ] **Step 2: Confirm fail mode**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementsCardList.test.tsx
```

Expected: FAIL with `Cannot find module './MovementsCardList'`.

### Step 3: Implement MovementsCardList

- [ ] **Step 3: Create the component**

Create `ProjectCeres.Client/src/app/features/movements/MovementsCardList.tsx`:

```tsx
import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { Numeric } from '@/components/Numeric';
import { MovementClearedToggle } from './MovementClearedToggle';
import { MovementRowMenu } from './MovementRowMenu';
import type { MovementListItemDto } from './movements-api';
import { MOVEMENT_TYPE_LABEL } from './movement-type-display';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = { items: MovementListItemDto[]; onRefetch: () => void };

function amountColor(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') {
    if (item.categoryTypeName === 'Income') return 'text-success';
    if (item.categoryTypeName === 'Expense') return 'text-destructive';
    return 'text-foreground';
  }
  if (item.movementType === 'Transfer') return 'text-chart-6';
  if (item.movementType === 'LiabilityPayment') return 'text-chart-7';
  return 'text-foreground';
}

function typePill(item: MovementListItemDto) {
  if (item.movementType === 'Transaction') {
    return <Badge variant="info">{MOVEMENT_TYPE_LABEL.Transaction}</Badge>;
  }
  if (item.movementType === 'Transfer') {
    return <Badge className="bg-chart-6/10 text-chart-6">{MOVEMENT_TYPE_LABEL.Transfer}</Badge>;
  }
  return <Badge className="bg-chart-7/10 text-chart-7">{MOVEMENT_TYPE_LABEL.LiabilityPayment}</Badge>;
}

function accountLine(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') return item.accountName ?? '';
  if (item.movementType === 'Transfer') return `${item.sourceAccountName} → ${item.destAccountName}`;
  return `${item.assetAccountName} → ${item.liabilityAccountName}`;
}

function primaryLine(item: MovementListItemDto): string {
  if (item.description) return item.description;
  if (item.movementType === 'Transaction' && item.categoryName) return item.categoryName;
  return MOVEMENT_TYPE_LABEL[item.movementType];
}

export function MovementsCardList({ items, onRefetch }: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const dateFormat = settings.data?.dateFormat;

  return (
    <ul className="space-y-2" role="list">
      {items.map((item) => {
        const dateLabel = formatDate(item.date, dateFormat);
        return (
          <li key={`${item.movementType}-${item.id}`}>
            <article
              className={cn(
                'group relative rounded-lg border border-border bg-card text-card-foreground',
                'transition-colors [transition-duration:var(--motion-duration-base)]',
                'hover:bg-accent/40 focus-within:ring-2 focus-within:ring-ring',
              )}
              style={{ viewTransitionName: `movement-row-${item.id}` }}
            >
              <Link
                to={`/movements/${item.id}/edit`}
                aria-label={`Edit ${MOVEMENT_TYPE_LABEL[item.movementType]} on ${dateLabel}`}
                className="block px-4 py-3 focus:outline-none"
              >
                {/* Line 1: type pill + date | amount */}
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2 text-xs text-muted-foreground">
                    {typePill(item)}
                    <span>{dateLabel}</span>
                  </div>
                  <Numeric className={cn('text-sm font-medium', amountColor(item))}>
                    {item.currencySymbol} {formatNumberForDisplay(item.amount, numberFormat)}
                  </Numeric>
                </div>
                {/* Line 2: primary text (description, fallback to category or type label) */}
                <div className="mt-1.5 pr-10 text-sm font-medium">
                  {primaryLine(item)}
                </div>
                {/* Line 3: account info, status badge sits on the right (rendered absolutely outside the link) */}
                <div className="mt-1.5 pr-28 text-xs text-muted-foreground">
                  {accountLine(item)}
                </div>
              </Link>
              {/* ⋮ menu — top-right corner, isolated from card-body tap */}
              <div
                className="absolute right-2 top-2 flex min-h-11 min-w-11 items-center justify-center"
                onClick={(e) => e.stopPropagation()}
              >
                <MovementRowMenu
                  movementId={item.id}
                  movementType={item.movementType}
                  isOpeningBalance={item.isOpeningBalance}
                  onDeleted={onRefetch}
                />
              </div>
              {/* Status badge — bottom-right, isolated from card-body tap */}
              <div
                className="absolute right-3 bottom-2 flex min-h-11 min-w-11 items-center justify-end"
                onClick={(e) => e.stopPropagation()}
              >
                <MovementClearedToggle
                  id={item.id}
                  type={item.movementType}
                  isCleared={item.isCleared}
                />
              </div>
            </article>
          </li>
        );
      })}
    </ul>
  );
}
```

### Step 4: Run the tests to verify they pass

- [ ] **Step 4: Confirm green**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementsCardList.test.tsx
```

Expected: 11/11 passing.

If any test fails, the most likely causes are:

- **The "stopPropagation" test (Test 9) fails** — the `onClick` on the wrapper `<div>` runs but the underlying `<button>`'s click also bubbles. React synthetic events bubble through the React tree, not the DOM tree. The `stopPropagation` on the wrapper catches both the React-synthetic and DOM-native events, so this should hold; if it doesn't, change the `onClick` to use `onClickCapture` so it fires before the `<button>`'s own click handler.
- **The "list role" test (Test 11) fails** — if `<ul>` doesn't expose `role="list"` because Tailwind's reset-CSS strips it (some `list-style: none` rules do this). The component already passes `role="list"` explicitly so this is belt-and-braces.

---

## Task 2: Wire breakpoint switch in MovementsLayout

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`

### Step 1: Add the import + hook

- [ ] **Step 1: Edit `MovementsLayout.tsx`**

Open `ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx`. Find the existing `MovementsTable` import (line 25):

```tsx
import { MovementsTable } from './MovementsTable';
```

Add the new imports below it:

```tsx
import { MovementsCardList } from './MovementsCardList';
import { useMediaQuery } from '../../lib/use-media-query';
```

### Step 2: Compute the breakpoint inside the component

- [ ] **Step 2: Add `useMediaQuery` call**

Find where `refetch`, `data`, `error`, `loading` are destructured from `useApi(...)` (search for `useApi<MovementsPageDto>`). Just below that line, add:

```tsx
const isDesktop = useMediaQuery('(min-width: 768px)');
```

Place it inside the component body, near the top with other hook calls.

### Step 3: Swap the render branch

- [ ] **Step 3: Replace `<MovementsTable>` with the conditional**

Find line ~186 (the only `<MovementsTable>` usage):

```tsx
            <MovementsTable items={data.items} onRefetch={refetch} />
```

Replace with:

```tsx
            {isDesktop
              ? <MovementsTable items={data.items} onRefetch={refetch} />
              : <MovementsCardList items={data.items} onRefetch={refetch} />}
```

### Step 4: Run the existing MovementsLayout tests

- [ ] **Step 4: Confirm no regression**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run src/app/features/movements/MovementsLayout.test.tsx
```

Expected: green. The existing tests assume jsdom's `matchMedia` returns `false` by default (the `test-setup.ts` polyfill returns `matches: false`). With `matches: false`, `isDesktop = false`, so the layout renders the `<MovementsCardList>`. Any test that asserts against table-specific markup (e.g., looking for `<table>` or `<th>` elements) will need to opt into desktop mode by re-mocking matchMedia for that test.

If a test fails because it can't find a `<table>` element it expected, do the minimum fix: at the top of that one test, override the matchMedia mock to return `true` for `(min-width: 768px)`:

```tsx
beforeEach(() => {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: query === '(min-width: 768px)',
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => true,
    }),
  });
});
```

Don't change the global mock in `test-setup.ts` — keep it returning `false` so the new card-list path is exercised by default in other tests.

### Step 5: Run the full client test suite

- [ ] **Step 5: Confirm full suite green**

Run (foreground per project memory):
```bash
cd <repo>/ProjectCeres.Client
pnpm test --run
```

Expected: 832 prior + 11 new = 843 passing. If a flaky test (`BudgetEdit > discriminator=…` or `MovementForm > Test 14`) fails, re-run once — both have surfaced as intermittent earlier today and pass on retry.

### Step 6: Run the production build

- [ ] **Step 6: Build**

Run:
```bash
cd <repo>/ProjectCeres.Client
pnpm build
```

Expected: clean.

### Step 7: Commit

- [ ] **Step 7: Commit**

Run:
```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/movements/MovementsCardList.tsx ProjectCeres.Client/src/app/features/movements/MovementsCardList.test.tsx ProjectCeres.Client/src/app/features/movements/MovementsLayout.tsx
git commit -m "feat(movements): card-list layout for sub-md viewports" -m "Replaces the desktop table with a stacked card layout below 768px. Each card shows date + type pill + amount on line 1, description on line 2 (with category fallback when description is null), account(s) on line 3. Status badge sits bottom-right and the row menu top-right; both have isolated tap zones via stopPropagation so card-body tap navigates to Edit. Desktop table renders unchanged at md+. New component mirrors MovementsTable's Props contract so MovementsLayout can swap by reference based on useMediaQuery('(min-width: 768px)')."
```

NO `Co-Authored-By:` trailer.

---

## Task 3: Browser-pass verification

After Task 2's commit lands, do a quick manual verification.

### Step 1: Start the servers

- [ ] **Step 1: Start servers**

Terminal 1:
```bash
cd <repo>
dotnet run --project ProjectCeres
```

Terminal 2:
```bash
cd <repo>/ProjectCeres.Client
pnpm dev
```

### Step 2: Verify desktop unchanged

- [ ] **Step 2: Open `https://localhost:7081/app/movements`**

At desktop width (≥ 768 px) the existing 8-column table renders exactly as before. Type filter dropdown, status badge, row menu, pagination all work. No visual regression.

### Step 3: Verify mobile cards

- [ ] **Step 3: Resize to iPhone SE (375 px) via DevTools**

Confirm:
- The table is gone; cards render full-width.
- Each card shows the type pill, date, amount, description (or category fallback), account info, status badge, ⋮ menu.
- Amount color matches (income green, expense red, transfer chart-6, debt payment chart-7).
- No horizontal scroll on the page.
- Cards have ~80 px height; ~10 visible per screen on iPhone SE.

### Step 4: Tap behavior

- [ ] **Step 4: Tap each interactive zone**

- Tap the status badge → status flips, no navigation.
- Tap the ⋮ menu → dropdown opens, no navigation.
- Tap anywhere else on the card → navigates to `/app/movements/{id}/edit`.

### Step 5: Resize across the breakpoint

- [ ] **Step 5: Drag DevTools width slowly across 768 px**

Above 768 px → table. Below 768 px → cards. Swap should be clean (one frame) without horizontal-scroll flash.

### Step 6: Reload & sign off

- [ ] **Step 6: Hard reload at 375 px**

Cmd+Shift+R on the SPA at 375 px. Cards should render directly with no flash of table.

If everything is good, the implementation is done. If anything looks off, open a follow-up issue with the specific viewport, browser, and screenshot.

---

## Self-review notes (for the engineer)

- **Spec coverage:**
  - § Card structure → implemented in Task 1's `MovementsCardList.tsx` JSX.
  - § Component contract → matched (same `Props` shape).
  - § Markup → `<ul>` + `<li>` + `<article>` + `<Link>` + absolutely-positioned action buttons.
  - § Breakpoint switching → Task 2's `useMediaQuery` + ternary at line 186.
  - § Edit URL → bare `/movements/${id}/edit` (no query string).
  - § Action-zone tap isolation → wrapper `<div onClick={(e) => e.stopPropagation()}>` around toggle and menu.
  - § Accessibility → `role="list"`, `<article>` per item, focus-within ring, semantic `<Link>`, aria-label on link.
  - § Tests → 11 cases in Task 1, covering rendering, type variants, fallbacks, link href, aria-label, stopPropagation, list role.
- **Out of scope (per spec):** UX iteration on Detail view vs. Edit, applying the same pattern to other tables, infinite scroll, swipe gestures.
- **Stay-on-main:** Per project memory, no branches or worktrees. Commit straight to `main`.
- **No `Co-Authored-By` trailer.** Per project memory.
- **No `git push` suggestions.** This repo has no remote.
