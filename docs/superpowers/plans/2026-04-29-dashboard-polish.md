# Dashboard Polish + Design System v1.1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Polish the just-shipped Dashboard to ship-quality (fix duplicate titles, broken Health card layout, weak empty states, table styling) and add three reusable stat primitives + one accessibility fix to `CardTitle` so future SPA pages inherit a consistent shape.

**Architecture:** Three layers. (1) Patch shadcn `card.tsx` so `CardTitle` renders `<h3>` with `font-semibold` (one-line element + className edit, affects every Card consumer). (2) Add three small presentational primitives at `@/components/`: `StatTile`, `StatRow`, `EquationRow`. (3) Refactor the dashboard cards + the Razor-island budget bar components to consume the new primitives and drop their internal Card chrome.

**Tech Stack:** React 19 + TypeScript, Tailwind v4, shadcn/ui (`base-nova`), Vitest + React Testing Library, Lucide icons.

**Spec:** `docs/superpowers/specs/2026-04-29-dashboard-polish-design.md`. Read it before starting any task.

**Scope boundary:** No new features, no new endpoints, no new pages. The Razor `Index.cshtml` view is left untouched and a small visual regression in its embedded budget-bar islands is accepted (the view is being deleted in Dashboard Phase 2).

**Spec divergences locked in here** (the implementer should follow this plan, not the spec, where they conflict):

- **Card padding standardization (spec §3) is dropped.** The existing `card.tsx` has a sophisticated grid-based `CardHeader` and a `Card` with `py-4` + per-slot `px-4`. Wholesale-replacing it with `p-6` would break the design-system showcase and force changes across every existing consumer. **What this plan keeps:** patching `CardTitle` to `<h3>` + `font-semibold` (the accessibility and weight fixes). **What this plan drops:** any standardization of `CardHeader` / `CardContent` padding. Per-callsite alignment of "View all →" links is handled by adding `flex items-baseline justify-between w-full` to the dashboard wrappers' `CardHeader` (not by changing the default).

---

## File Structure

**Created (React client):**
- `ProjectCeres.Client/src/components/StatTile.tsx`
- `ProjectCeres.Client/src/components/StatRow.tsx`
- `ProjectCeres.Client/src/components/EquationRow.tsx`

**Modified (React client):**
- `ProjectCeres.Client/src/components/ui/card.tsx` — `CardTitle` element changed from `<div>` to `<h3>`; weight bumped from `font-medium` to `font-semibold`.
- `ProjectCeres.Client/src/components/CategoryBudgetBars.tsx` — drop outer Card chrome; render bare body.
- `ProjectCeres.Client/src/components/GoalBudgetBars.tsx` — drop outer Card chrome; render bare body.
- `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx` — retrofit to use `<StatRow>`.
- `ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx` — single-currency uses `<StatRow>`; multi-currency table gets zebra stripes + bold headers + cell side-padding.
- `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx` — asymmetric grid + equation rows use `<EquationRow>` + divider sequencing fix.
- `ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx` — empty state shows `CheckCircle2` + "All caught up".
- `ProjectCeres.Client/src/design-system/pages/Components.tsx` — add Stat Components showcase section.

**Test files updated:**
- `ProjectCeres.Client/src/app/features/dashboard/RemindersCard.test.tsx` — change `'No reminders due.'` to `'All caught up'`.

**Test files expected to stay green without change** — confirmed during implementation:
- `MtdCard.test.tsx`, `NetWorthCard.test.tsx`, `FinancialHealthCard.test.tsx`, `KpiStrip.test.tsx`, `Dashboard.test.tsx`, `App.test.tsx`, `Sidebar.test.tsx`, `MobileDrawer.test.tsx`, `AvatarMenu.test.tsx`, `PagePlaceholder.test.tsx`, `CategoryBudgetBars.test.tsx`, `GoalBudgetBars.test.tsx`.

---

## Task 1: Patch `CardTitle` to render `<h3>` with `font-semibold`

**Files:**
- Modify: `ProjectCeres.Client/src/components/ui/card.tsx`

A two-line change to `card.tsx`. Affects every Card consumer in the app — design-system showcase, dashboard cards, top-bar dropdowns, future pages.

- [ ] **Step 1: Read the current `CardTitle` definition**

```bash
grep -A 10 'function CardTitle' ProjectCeres.Client/src/components/ui/card.tsx
```

Confirm the function looks like:

```tsx
function CardTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-title"
      className={cn(
        "font-heading text-base leading-snug font-medium group-data-[size=sm]/card:text-sm",
        className
      )}
      {...props}
    />
  )
}
```

- [ ] **Step 2: Replace the function**

In `ProjectCeres.Client/src/components/ui/card.tsx`, replace the `CardTitle` function with:

```tsx
function CardTitle({ className, ...props }: React.ComponentProps<"h3">) {
  return (
    <h3
      data-slot="card-title"
      className={cn(
        "font-heading text-base leading-snug font-semibold group-data-[size=sm]/card:text-sm",
        className
      )}
      {...props}
    />
  )
}
```

Two changes from the original:
- `<div>` → `<h3>`, plus the matching `React.ComponentProps<"h3">` type.
- `font-medium` → `font-semibold`.

Everything else (`font-heading`, `text-base`, `leading-snug`, the `data-[size=sm]` override, the `cn(...)` merge) stays identical.

- [ ] **Step 3: Run the full client test suite**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS. Some existing tests use `screen.getByRole('heading', { level: ... })`. The change from `<div>` to `<h3>` may cause an existing test that wasn't expecting a heading-roled element to now find one, or an existing test that expected `level: 1` may now find an additional `level: 3` entry. If a test fails, read the failure carefully — the test itself is asserting behavior we just changed (more headings exist now), and the fix is usually to scope the query (e.g., `getByRole('heading', { level: 1 })` instead of `getByRole('heading')`).

If `App.test.tsx` fails because each route's PagePlaceholder is wrapped in a `Card` whose `CardTitle` is now an `<h3>` — that's actually fine because PagePlaceholder doesn't use `CardTitle`, it uses a raw `<h1>`. The Card primitive elsewhere (in showcase, dashboard) gets the new `<h3>`.

- [ ] **Step 4: Run the build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS — no TypeScript errors from the prop-type change (`HTMLAttributes<HTMLHeadingElement>` is a strict subset of what `<div>` accepted; only callers passing `<div>`-only props would break, and there shouldn't be any).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/card.tsx
git commit -m "fix(design-system): CardTitle renders h3 with font-semibold for hierarchy + a11y"
```

---

## Task 2: Add `StatTile` primitive

**Files:**
- Create: `ProjectCeres.Client/src/components/StatTile.tsx`

Vertical KPI tile. Small uppercase label on top, value below with caller-controlled className.

- [ ] **Step 1: Create the file**

Create `ProjectCeres.Client/src/components/StatTile.tsx`:

```tsx
import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type StatTileProps = {
  /** Short uppercase descriptor shown above the value. */
  label: string;
  /** The value. Pass <Numeric>...</Numeric> for tabular numerics. */
  value: ReactNode;
  /** Optional className applied to the value's wrapping div (color, weight, size). */
  valueClassName?: string;
};

/**
 * Vertical KPI tile. Use for prominent metrics where the value
 * deserves visual weight (dashboards, summary blocks).
 */
export function StatTile({ label, value, valueClassName }: StatTileProps) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium">
        {label}
      </div>
      <div className={cn(valueClassName)}>{value}</div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/components/StatTile.tsx
git commit -m "feat(design-system): add StatTile vertical KPI primitive"
```

---

## Task 3: Add `StatRow` primitive

**Files:**
- Create: `ProjectCeres.Client/src/components/StatRow.tsx`

Inline labelled value. Label + value baseline-aligned in a single row.

- [ ] **Step 1: Create the file**

Create `ProjectCeres.Client/src/components/StatRow.tsx`:

```tsx
import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type StatRowProps = {
  label: string;
  value: ReactNode;
  /** Optional className applied to the value's wrapping span. */
  valueClassName?: string;
};

/**
 * Inline labelled value. Label on the left, value on the right,
 * baseline-aligned. Wrap multiple StatRows in a `<dl className="space-y-2">`
 * for grouped stat displays (e.g. MTD card).
 */
export function StatRow({ label, value, valueClassName }: StatRowProps) {
  return (
    <div className="flex items-baseline justify-between text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn(valueClassName)}>{value}</span>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/components/StatRow.tsx
git commit -m "feat(design-system): add StatRow inline labelled-value primitive"
```

---

## Task 4: Add `EquationRow` primitive

**Files:**
- Create: `ProjectCeres.Client/src/components/EquationRow.tsx`

Smaller, tighter version of `StatRow` for stacked equation rows.

- [ ] **Step 1: Create the file**

Create `ProjectCeres.Client/src/components/EquationRow.tsx`:

```tsx
import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type EquationRowProps = {
  label: string;
  value: ReactNode;
  /** Optional className applied to the value's wrapping span. */
  valueClassName?: string;
};

/**
 * Compact equation row. Both label and value are muted by default
 * (the row is informational, not a headline). Use inside dense
 * vertical stacks like the Spendable Balance breakdown.
 *
 * For headline rows (e.g. "Available today" in the equation),
 * pass a valueClassName like "text-base font-bold text-success"
 * to override the muted default.
 */
export function EquationRow({ label, value, valueClassName }: EquationRowProps) {
  return (
    <div className="flex justify-between text-[11px] text-muted-foreground">
      <span>{label}</span>
      <span className={cn(valueClassName)}>{value}</span>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/components/EquationRow.tsx
git commit -m "feat(design-system): add EquationRow compact primitive"
```

---

## Task 5: Refactor `CategoryBudgetBars` to render bare body

**Files:**
- Modify: `ProjectCeres.Client/src/components/CategoryBudgetBars.tsx`

Drop the outer `<Card>` + `<CardHeader>` + `<CardTitle>` so the dashboard wrapper's Card is the only Card in the tree. Existing tests don't assert on the Card chrome (they check `getByText('Housing')` etc.) so they keep passing.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/components/CategoryBudgetBars.tsx
```

Note the imports and the body structure inside `CardContent`.

- [ ] **Step 2: Replace the file**

Replace the entire contents with:

```tsx
import { useEffect, useState } from 'react'
import { Progress } from '@/components/ui/progress'
import { Badge } from '@/components/ui/badge'

interface CategoryBudgetItem {
  id: string
  categoryName: string
  currencyCode: string
  currencySymbol: string
  spent: number
  limit: number
  percentUsed: number
}

function statusVariant(pct: number): 'default' | 'secondary' | 'destructive' {
  if (pct >= 100) return 'destructive'
  if (pct >= 80) return 'secondary'
  return 'default'
}

export function CategoryBudgetBars() {
  const [items, setItems] = useState<CategoryBudgetItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/category-budgets')
      .then(r => r.json())
      .then(data => { setItems(data); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  if (loading) return <p className="text-sm text-muted-foreground">Loading budgets…</p>
  if (items.length === 0) return <p className="text-sm text-muted-foreground">No active category budgets.</p>

  return (
    <div className="space-y-4">
      {items.map(item => (
        <div key={item.id} className="space-y-1.5">
          <div className="flex items-center justify-between text-sm">
            <span className="font-medium">{item.categoryName}</span>
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground" style={{ fontFamily: "'IBM Plex Mono', ui-monospace, monospace" }}>
                {item.currencySymbol || item.currencyCode} {item.spent.toFixed(2)} / {item.currencySymbol || item.currencyCode} {item.limit.toFixed(2)}
              </span>
              <Badge variant={statusVariant(item.percentUsed)} className="text-xs">{item.percentUsed}%</Badge>
            </div>
          </div>
          <Progress value={Math.min(item.percentUsed, 100)} className="h-2" />
        </div>
      ))}
    </div>
  )
}
```

Key changes from the original:
- Removed the `Card`, `CardContent`, `CardHeader`, `CardTitle` imports.
- The component returns either a `<p>` (loading/empty) or a `<div className="space-y-4">` containing the bars list — no Card.

If the original file had additional content inside `CardContent` (e.g., `<Badge>` content) that I missed in my paste, preserve it inside the new `<div className="space-y-4">`. Read the file in Step 1 carefully and keep every visible row item intact.

- [ ] **Step 3: Run the existing tests**

```bash
pnpm --dir ProjectCeres.Client test src/components/CategoryBudgetBars.test.tsx
```

Expected: PASS — the tests check body content (`getByText('Housing')`, `getByText(/400/)` etc.), not Card chrome.

- [ ] **Step 4: Run all client tests**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — no other test should reference `CategoryBudgetBars`'s Card.

- [ ] **Step 5: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/components/CategoryBudgetBars.tsx
git commit -m "refactor(category-budget-bars): render bare body so caller owns Card chrome"
```

---

## Task 6: Refactor `GoalBudgetBars` to render bare body

**Files:**
- Modify: `ProjectCeres.Client/src/components/GoalBudgetBars.tsx`

Same pattern as Task 5.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/components/GoalBudgetBars.tsx
```

- [ ] **Step 2: Replace the file**

Replace the entire contents with:

```tsx
import { useEffect, useState } from 'react'
import { Progress } from '@/components/ui/progress'

interface GoalBudgetItem {
  id: string
  name: string
  goalType: string
  amountProgress: number
  targetAmount: number
  percentUsed: number
  currencyCode: string
  currencySymbol: string
}

export function GoalBudgetBars() {
  const [items, setItems] = useState<GoalBudgetItem[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/goal-budgets')
      .then(r => r.json())
      .then(data => { setItems(data); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  if (loading) return <p className="text-sm text-muted-foreground">Loading goals…</p>
  if (items.length === 0) return <p className="text-sm text-muted-foreground">No active goal budgets.</p>

  return (
    <div className="space-y-4">
      {items.map(item => (
        <div key={item.id} className="space-y-1.5">
          <div className="flex items-center justify-between text-sm">
            <div className="flex items-center gap-2">
              <span className="font-medium">{item.name}</span>
              <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800">{item.goalType}</span>
            </div>
            <span className="text-muted-foreground" style={{ fontFamily: "'IBM Plex Mono', ui-monospace, monospace" }}>
              {item.currencySymbol || item.currencyCode} {item.amountProgress.toFixed(2)} / {item.currencySymbol || item.currencyCode} {item.targetAmount.toFixed(2)}
            </span>
          </div>
          <Progress value={Math.min(item.percentUsed, 100)} className="h-2" />
          <p className="text-xs text-muted-foreground">{item.percentUsed}% toward goal</p>
        </div>
      ))}
    </div>
  )
}
```

If the original file had additional content I missed, preserve it inside the new `<div className="space-y-4">`.

- [ ] **Step 3: Run the existing tests**

```bash
pnpm --dir ProjectCeres.Client test src/components/GoalBudgetBars.test.tsx
```

Expected: PASS.

- [ ] **Step 4: Run all client tests**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS.

- [ ] **Step 5: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/components/GoalBudgetBars.tsx
git commit -m "refactor(goal-budget-bars): render bare body so caller owns Card chrome"
```

---

## Task 7: Retrofit `MtdCard` to use `<StatRow>`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx`

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx
```

- [ ] **Step 2: Replace the data branch**

Replace the entire contents of `MtdCard.tsx` with:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatRow } from '@/components/StatRow';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type SummaryDto } from './api';

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

export function MtdCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Month to Date</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && (
          <div className="space-y-2">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
          </div>
        )}
        {error && <CardError section="Month to Date" onRetry={refetch} />}
        {data && data.mtd.income === 0 && data.mtd.expenses === 0 && (
          <p className="text-sm text-muted-foreground">No transactions this month yet.</p>
        )}
        {data && (data.mtd.income !== 0 || data.mtd.expenses !== 0) && (
          <dl className="space-y-2">
            <StatRow
              label="Income"
              value={<Numeric className="text-success">{data.mtd.currencySymbol} {data.mtd.income.toFixed(2)}</Numeric>}
            />
            <StatRow
              label="Expenses"
              value={<Numeric className="text-destructive">{data.mtd.currencySymbol} {data.mtd.expenses.toFixed(2)}</Numeric>}
            />
            <StatRow
              label="Savings Rate"
              value={<Numeric>{formatPercent(data.mtd.savingsRate)}</Numeric>}
            />
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
```

Key changes:
- Added `import { StatRow } from '@/components/StatRow';`.
- Replaced the three hand-rolled `<div className="flex items-baseline justify-between">` rows with `<StatRow>` instances.
- The color-class assertions in the test still pass because the `<Numeric className="text-success">` and `<Numeric className="text-destructive">` elements are still found by `screen.findByText(/3200/)`.

- [ ] **Step 3: Run the test**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/MtdCard.test.tsx
```

Expected: PASS — all 4 tests still pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx
git commit -m "refactor(dashboard): MtdCard uses StatRow primitive"
```

---

## Task 8: Retrofit `NetWorthCard` single-currency to `<StatRow>` + table polish

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx`

Two changes in one task: single-currency variant uses `<StatRow>`; multi-currency table gets zebra stripes + proper headers + cell side-padding.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx
```

- [ ] **Step 2: Replace the file**

Replace the entire contents with:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatRow } from '@/components/StatRow';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type NetWorthEntry, type SummaryDto } from './api';

export function NetWorthCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Net Worth</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && (
          <div className="space-y-2">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
          </div>
        )}
        {error && <CardError section="Net Worth" onRetry={refetch} />}
        {data && data.netWorth.length === 0 && (
          <p className="text-sm text-muted-foreground">
            No accounts yet. Add one to see your net worth.
          </p>
        )}
        {data && data.netWorth.length === 1 && <SingleCurrency entry={data.netWorth[0]} />}
        {data && data.netWorth.length > 1 && <MultiCurrency entries={data.netWorth} />}
      </CardContent>
    </Card>
  );
}

function netWorthClass(value: number): string {
  return value >= 0 ? 'text-success' : 'text-destructive';
}

function SingleCurrency({ entry }: { entry: NetWorthEntry }) {
  return (
    <dl className="space-y-2">
      <StatRow
        label="Assets"
        value={<Numeric>{entry.currencySymbol} {entry.assets.toFixed(2)}</Numeric>}
      />
      <StatRow
        label="Liabilities"
        value={<Numeric>{entry.currencySymbol} {entry.liabilities.toFixed(2)}</Numeric>}
      />
      <StatRow
        label="Net Worth"
        value={
          <Numeric className={netWorthClass(entry.netWorth)}>
            {entry.currencySymbol} {entry.netWorth.toFixed(2)}
          </Numeric>
        }
      />
    </dl>
  );
}

function MultiCurrency({ entries }: { entries: NetWorthEntry[] }) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-border text-left text-muted-foreground">
          <th className="py-2 px-2">Currency</th>
          <th className="py-2 px-2 text-right">Assets</th>
          <th className="py-2 px-2 text-right">Liabilities</th>
          <th className="py-2 px-2 text-right">Net Worth</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.currencyCode} className="border-b border-border last:border-0 even:bg-muted/30">
            <td className="py-2 px-2">{entry.currencyCode}</td>
            <td className="py-2 px-2 text-right"><Numeric>{entry.assets.toFixed(2)}</Numeric></td>
            <td className="py-2 px-2 text-right"><Numeric>{entry.liabilities.toFixed(2)}</Numeric></td>
            <td className="py-2 px-2 text-right">
              <Numeric className={netWorthClass(entry.netWorth)}>
                {entry.currencySymbol} {entry.netWorth.toFixed(2)}
              </Numeric>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
```

Changes from the original:
- Added `import { StatRow } from '@/components/StatRow';`.
- `SingleCurrency` uses three `<StatRow>` instances instead of hand-rolled flex rows.
- `MultiCurrency`: removed `font-normal` overrides on `<th>`; added `even:bg-muted/30` to row classes; added `px-2` to all `<th>` and `<td>` cells.

- [ ] **Step 3: Run the test**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/NetWorthCard.test.tsx
```

Expected: PASS — all 5 tests still pass. The behavioural assertions (empty state, single vs multi rendering, EUR/USD rows in multi, negative-color on net worth) are unaffected by these style changes.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx
git commit -m "refactor(dashboard): NetWorthCard single-currency uses StatRow; multi-currency table polished"
```

---

## Task 9: Health card — asymmetric grid + EquationRow retrofit + divider fix

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx`

Three changes in one task because they're all in the same file and affect the same render output.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx
```

- [ ] **Step 2: Replace the file**

Replace the entire contents with:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { EquationRow } from '@/components/EquationRow';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { HEALTH_URL, type HealthDto } from './api';
import {
  availableTodayClass,
  burnRateClass,
  incomeDeltaClass,
  runwayClass,
  safeToSpendClass,
} from './dashboardViewHelper';

export function FinancialHealthCard() {
  const { data, error, loading, refetch } = useApi<HealthDto>(HEALTH_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Financial Health</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-32 w-full" />}
        {error && <CardError section="Financial Health" onRetry={refetch} />}
        {data && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-[1.6fr_1fr_1fr_1fr] gap-6">
            <SpendablePanel data={data} />
            <RunwayPanel data={data} />
            <IncomeDeltaPanel data={data} />
            <BurnRatePanel data={data} />
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function PanelLabel({ children }: { children: string }) {
  return (
    <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium mb-2">
      {children}
    </div>
  );
}

function PanelEmpty({ children }: { children: string }) {
  return <div className="text-xs italic text-muted-foreground">{children}</div>;
}

function SpendablePanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Spendable Balance</PanelLabel>
      {data.availableToday === null ? (
        <PanelEmpty>No asset accounts found</PanelEmpty>
      ) : (
        <SpendableEquation data={data} />
      )}
    </div>
  );
}

function SpendableEquation({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  const liquid = (data.availableToday ?? 0) + (data.imminentBills ?? 0);
  const hasImminent = (data.imminentBills ?? 0) !== 0;
  const hasLater = (data.laterBills ?? 0) !== 0;
  const hasReserve = (data.budgetReserve ?? 0) !== 0;
  const showSafeToSpend = data.safeToSpend !== null && (hasLater || hasReserve);

  return (
    <div className="space-y-1.5">
      <EquationRow label="Liquid" value={<Numeric>{sym} {liquid.toFixed(2)}</Numeric>} />
      {hasImminent && (
        <EquationRow
          label="Bills due (7 days)"
          value={<Numeric>−{sym} {(data.imminentBills ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      <div className="border-t border-border mt-1 pt-2">
        <EquationRow
          label="Available today"
          value={
            <Numeric className={`text-base font-bold ${availableTodayClass(data.availableToday!)}`}>
              {sym} {data.availableToday!.toFixed(2)}
            </Numeric>
          }
        />
      </div>
      {hasLater && (
        <EquationRow
          label="Bills later this month"
          value={<Numeric>−{sym} {(data.laterBills ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      {hasReserve && (
        <EquationRow
          label="Budget reserved"
          value={<Numeric>−{sym} {(data.budgetReserve ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      {showSafeToSpend && (
        <div className="border-t border-border mt-1 pt-2">
          <EquationRow
            label="Safe to spend"
            value={
              <Numeric className={`text-sm font-semibold ${safeToSpendClass(data.safeToSpend!)}`}>
                {sym} {data.safeToSpend!.toFixed(2)}
              </Numeric>
            }
          />
        </div>
      )}
    </div>
  );
}

function RunwayPanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Runway</PanelLabel>
      {data.runwayMonths === null ? (
        <PanelEmpty>Needs 6 months of expense history</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${runwayClass(data.runwayMonths)}`}>
          {data.runwayMonths.toFixed(1)} mo
        </Numeric>
      )}
    </div>
  );
}

function IncomeDeltaPanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Income vs. Avg</PanelLabel>
      {data.incomeDeltaPercent === null ? (
        <PanelEmpty>Needs 6 months of income history</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${incomeDeltaClass(data.incomeDeltaPercent)}`}>
          {data.incomeDeltaPercent >= 0 ? '+' : ''}{(data.incomeDeltaPercent * 100).toFixed(1)}%
        </Numeric>
      )}
    </div>
  );
}

function BurnRatePanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Budget Burn Rate</PanelLabel>
      {data.budgetBurnRate === null ? (
        <PanelEmpty>No active category budgets</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${burnRateClass(data.budgetBurnRate)}`}>
          {(data.budgetBurnRate * 100).toFixed(1)}%
        </Numeric>
      )}
    </div>
  );
}
```

Changes from the original:
- Removed the local `Row` helper component (replaced by `<EquationRow>`).
- Outer grid: `lg:grid-cols-4` → `md:grid-cols-2 lg:grid-cols-[1.6fr_1fr_1fr_1fr]`.
- `SpendableEquation`: every "regular" row (Liquid, Bills due, Bills later, Budget reserved) uses `<EquationRow>` directly; the two headline rows ("Available today" and "Safe to spend") wrap an `<EquationRow>` in a `<div className="border-t border-border mt-1 pt-2">` which is the divider sequencing fix.

- [ ] **Step 3: Run the test**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/FinancialHealthCard.test.tsx
```

Expected: PASS — all 10 tests still pass. None of them assert on grid columns or pixel-level layout; all use text-based queries.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx
git commit -m "refactor(dashboard): FinancialHealthCard asymmetric grid + EquationRow + divider fix"
```

---

## Task 10: `RemindersCard` empty state — `CheckCircle2` + "All caught up"

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx`
- Modify: `ProjectCeres.Client/src/app/features/dashboard/RemindersCard.test.tsx`

- [ ] **Step 1: Update the test**

In `ProjectCeres.Client/src/app/features/dashboard/RemindersCard.test.tsx`, change:

```tsx
await screen.findByText('No reminders due.');
```

to:

```tsx
await screen.findByText('All caught up');
```

(There's only one occurrence — the "renders muted" test case.)

- [ ] **Step 2: Run the test to verify it FAILS**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/RemindersCard.test.tsx
```

Expected: FAIL — the empty-state branch still renders the old copy.

- [ ] **Step 3: Update `RemindersCard.tsx`**

Read the current file first:

```bash
cat ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx
```

Replace the empty-state branch. Find:

```tsx
{data && data.remindersDueCount === 0 && (
  <p className="text-sm text-muted-foreground">No reminders due.</p>
)}
```

Replace with:

```tsx
{data && data.remindersDueCount === 0 && (
  <p className="flex items-center gap-2 text-sm text-foreground">
    <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
    All caught up
  </p>
)}
```

Add the import at the top of the file:

```tsx
import { CheckCircle2 } from 'lucide-react';
```

The "data" branch (count > 0) is unchanged.

- [ ] **Step 4: Run the test to verify it PASSES**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/RemindersCard.test.tsx
```

Expected: PASS — all 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx \
        ProjectCeres.Client/src/app/features/dashboard/RemindersCard.test.tsx
git commit -m "feat(dashboard): RemindersCard empty state shows 'All caught up' with success icon"
```

---

## Task 11: Add Stat Components section to design-system showcase

**Files:**
- Modify: `ProjectCeres.Client/src/design-system/pages/Components.tsx`

Add a section demoing the three new primitives.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/design-system/pages/Components.tsx
```

Note the existing imports and the `<TooltipProvider>` wrapping pattern.

- [ ] **Step 2: Add the imports**

Add these imports at the top of the file (alongside the existing imports):

```tsx
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';
```

If `Numeric` is already imported, don't duplicate.

- [ ] **Step 3: Add the section**

Append this section as the LAST `<section>` inside the existing wrapper `<TooltipProvider>` (right before the closing `</div>` and `</TooltipProvider>`):

```tsx
        <section>
          <h2 className="mb-4 text-xl font-medium">Stat components</h2>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-6 max-w-3xl">
            <Card>
              <CardHeader><CardTitle>StatTile (vertical)</CardTitle></CardHeader>
              <CardContent>
                <StatTile
                  label="Total Spent"
                  value={<Numeric>€ 1,234.56</Numeric>}
                  valueClassName="text-2xl font-bold text-success"
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>StatRow (inline)</CardTitle></CardHeader>
              <CardContent>
                <dl className="space-y-2">
                  <StatRow label="Income" value={<Numeric className="text-success">€ 3,200.00</Numeric>} />
                  <StatRow label="Expenses" value={<Numeric className="text-destructive">€ 1,850.45</Numeric>} />
                  <StatRow label="Savings Rate" value={<Numeric>42.2%</Numeric>} />
                </dl>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>EquationRow (compact)</CardTitle></CardHeader>
              <CardContent>
                <div className="space-y-1">
                  <EquationRow label="Liquid" value={<Numeric>€ 1,200.20</Numeric>} />
                  <EquationRow label="Bills due (7 days)" value={<Numeric>−€ 770.00</Numeric>} />
                  <EquationRow
                    label="Available today"
                    value={<Numeric className="text-base font-bold text-success">€ 430.20</Numeric>}
                  />
                </div>
              </CardContent>
            </Card>
          </div>
        </section>
```

- [ ] **Step 4: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/design-system/pages/Components.tsx
git commit -m "docs(design-system): add Stat Components showcase (StatTile, StatRow, EquationRow)"
```

---

## Task 12: Final verification

- [ ] **Step 1: Full client build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS — all three Vite entries (`index.html`, `design-system.html`, `app.html`) emit cleanly.

- [ ] **Step 2: Full client tests**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — all tests still green. Test count should be unchanged from the previous baseline (no new tests were added; one assertion in `RemindersCard.test.tsx` was updated).

- [ ] **Step 3: Full .NET build + tests**

```bash
dotnet build
dotnet test
```

Expected: PASS for both. No backend changes in this plan.

- [ ] **Step 4: Manual smoke test — start the app**

In one terminal: `pnpm --dir ProjectCeres.Client dev`
In another: `dotnet run --project ProjectCeres --launch-profile https`

- [ ] **Step 5: Visit `/app/` and verify**

Open `https://localhost:7081/app/` (or `http://localhost:5248/app/` if you're skipping the cert dance). Verify:

- **Dashboard heading**: text-3xl, dominates as expected.
- **Card titles**: bold (font-semibold), `<h3>` inspectable in DevTools.
- **Financial Health card**: 4-panel asymmetric grid. Spendable Balance gets ~36% width on a wide screen; the three indicators ~22% each. Divider above "Available today" sits flush with the headline.
- **KPI strip**:
  - Net Worth single-currency uses tidy stat rows (Assets / Liabilities / Net Worth).
  - Net Worth multi-currency table: zebra-striped rows, bold column headers.
  - MTD card: Income/Expenses/Savings Rate with the expected colors.
  - Reminders card empty state: green check icon + "All caught up".
- **Category Budgets** and **Goal Budgets**: ONE title each (the dashboard wrapper's). No nested Card. Budget bars render normally inside.

- [ ] **Step 6: Visit `/design-system.html#/components`**

Verify the new "Stat components" section renders all three primitives.

- [ ] **Step 7: Visit Razor `/Dashboard`**

Open `https://localhost:7081/Dashboard`. Expected:
- Page renders without crash.
- The Category Budgets and Goal Budgets islands render as **bare body** (no Card chrome). This IS a small visual regression — accepted because the view is being deleted in Dashboard Phase 2.
- Other elements (Health snapshot partial, top KPI cards, charts) untouched.

- [ ] **Step 8: Stop the dev servers**

Ctrl+C both terminals. No commit needed — verification only.

---

## Out of scope (already noted, surfaced here for clarity)

- New `DataTable` primitive — wait for the Transactions page.
- Top-bar button color rebalancing — cosmetic, tune anytime.
- Coloring deductions in equation rows — intentional decision (deductions are neutral information).
- Razor `Index.cshtml` visual fixup — accept regression; view is being deleted.
- Dashboard Phase 2 (6 chart sections + Razor delete + 302 redirect).
- Card padding standardization — divergence from spec §3 explained in plan header. The existing `card.tsx` is more sophisticated than the spec assumed; per-callsite alignment is sufficient.
- Standalone tests for `StatTile` / `StatRow` / `EquationRow` — covered transitively by the dashboard card tests.
