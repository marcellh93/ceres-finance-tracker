# Design System v1.2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Codify the component patterns that emerged during the SPA migration so they stop drifting across new features, and extend the live `/design-system.html` showcase to cover them.

**Architecture:** Extend shadcn `<Badge>` with three semantic variants (success/warning/info) and migrate hand-rolled status badges in MovementsTable + MovementClearedToggle to use them. Extract the inline `<Tile>` from MtdCard into `src/components/Tile.tsx`. Move `<CardError>` from `src/app/features/dashboard/` to `src/app/components/` (its true home as a cross-feature primitive) and update its 10 import paths. Add a "Toasts" page and a "Patterns" page to the `/design-system.html` showcase. Move existing layout primitives (StatTile/StatRow/EquationRow) out of the showcase Components page into the new Patterns page. Update `docs/design-system.md` with three new sections (status badges, layout primitives, CardError) and a one-line skeleton convention.

**Tech Stack:** React 19 + TypeScript + Vite + Tailwind v4 + shadcn/ui (`base-nova`) + Sonner + Vitest/RTL.

**Spec:** `docs/superpowers/specs/2026-04-30-design-system-v1.2-design.md`

---

## File Map

**Frontend — modify:**
- `ProjectCeres.Client/src/components/ui/badge.tsx` — add success/warning/info variants
- `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx` — import Tile from `@/components/Tile`, remove inline definition
- `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx` — replace hand-rolled badges with `<Badge>`, drop `typeBadgeClass` lookup table
- `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx` — replace hand-rolled badge classes with `<Badge variant="success|warning">`
- `ProjectCeres.Client/src/design-system/App.tsx` — add Toasts + Patterns sidebar entries
- `ProjectCeres.Client/src/design-system/pages/Components.tsx` — add Status Badges section (7 variants); add Skeleton section (3 examples); REMOVE the existing "Stat components" section (moves to Patterns)
- 10 files: update `import { CardError }` paths

**Frontend — create:**
- `ProjectCeres.Client/src/components/Tile.tsx` + `Tile.test.tsx`
- `ProjectCeres.Client/src/app/components/CardError.tsx` (move target via `git mv`)
- `ProjectCeres.Client/src/design-system/pages/Toasts.tsx`
- `ProjectCeres.Client/src/design-system/pages/Patterns.tsx`

**Frontend — delete (via `git mv`):**
- `ProjectCeres.Client/src/app/features/dashboard/CardError.tsx` (moved to `src/app/components/`)

**Docs — modify:**
- `docs/design-system.md` — three new sections, Skeleton paragraph, shadcn-overrides table updates, Index updated

---

## Task Order Rationale

Foundations first (Badge variants + Tile extraction) so the migrations have something to migrate to. Then `<CardError>` move (touches 10 files but mechanically). Then call-site migrations (MovementsTable, MovementClearedToggle, MtdCard). Then showcase additions (independent of migrations). Then docs (depends on all the above being settled).

---

### Task 1: Extend `<Badge>` with semantic variants

**Files:**
- Modify: `ProjectCeres.Client/src/components/ui/badge.tsx`

- [ ] **Step 1: Add the three new variants**

In `ProjectCeres.Client/src/components/ui/badge.tsx`, find the `variant: { ... }` block inside `cva`. Add three entries (matching the soft-tinted recipe used in the existing `destructive` variant):

```tsx
const badgeVariants = cva(
  "group/badge inline-flex h-5 w-fit shrink-0 items-center justify-center gap-1 overflow-hidden rounded-4xl border border-transparent px-2 py-0.5 text-xs font-medium whitespace-nowrap transition-all focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 [&>svg]:pointer-events-none [&>svg]:size-3!",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
        secondary:
          "bg-secondary text-secondary-foreground [a]:hover:bg-secondary/80",
        destructive:
          "bg-destructive/10 text-destructive focus-visible:ring-destructive/20 dark:bg-destructive/20 dark:focus-visible:ring-destructive/40 [a]:hover:bg-destructive/20",
        success:
          "bg-success/10 text-success [a]:hover:bg-success/20",
        warning:
          "bg-warning/10 text-warning [a]:hover:bg-warning/20",
        info:
          "bg-info/10 text-info [a]:hover:bg-info/20",
        outline:
          "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
        ghost:
          "hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50",
        link: "text-primary underline-offset-4 hover:underline",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)
```

(The `success`, `warning`, `info` variants slot in after `destructive`, before `outline`. This is purely organizational — the order in the variants object doesn't affect runtime.)

- [ ] **Step 2: Verify type-check**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 3: Run client tests**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: All pass (existing badge tests, if any, are unaffected — new variants are additive).

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/badge.tsx
git commit -m "feat(badge): add success/warning/info semantic variants"
```

---

### Task 2: Extract `<Tile>` into `src/components/Tile.tsx`

**Files:**
- Create: `ProjectCeres.Client/src/components/Tile.tsx`
- Create: `ProjectCeres.Client/src/components/Tile.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/components/Tile.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Tile } from './Tile';

describe('Tile', () => {
  it('renders children inside a styled div', () => {
    render(<Tile>Hello</Tile>);
    expect(screen.getByText('Hello')).toBeInTheDocument();
  });

  it('applies the default surface classes', () => {
    const { container } = render(<Tile>X</Tile>);
    const div = container.firstChild as HTMLElement;
    expect(div.className).toContain('rounded-md');
    expect(div.className).toContain('bg-muted/40');
    expect(div.className).toContain('p-4');
  });

  it('appends caller-provided className', () => {
    const { container } = render(<Tile className="custom-class">X</Tile>);
    const div = container.firstChild as HTMLElement;
    expect(div.className).toContain('custom-class');
    expect(div.className).toContain('bg-muted/40');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/components/Tile.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `Tile`**

Create `ProjectCeres.Client/src/components/Tile.tsx`:

```tsx
import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

type TileProps = {
  children: ReactNode;
  className?: string;
};

/**
 * KPI surface wrapper. Use for grouping small numeric tiles
 * (e.g. inside MTD card). Applies a muted surface, rounded
 * corners, and consistent padding.
 */
export function Tile({ children, className }: TileProps) {
  return <div className={cn('rounded-md bg-muted/40 p-4', className)}>{children}</div>;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/components/Tile.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/components/Tile.tsx ProjectCeres.Client/src/components/Tile.test.tsx
git commit -m "feat(layout): extract Tile primitive from MtdCard"
```

---

### Task 3: Adopt extracted `<Tile>` in `MtdCard.tsx`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx`

- [ ] **Step 1: Replace inline `Tile` with import**

In `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx`:

a) Add the import at the top (alongside the other `@/components` imports):

```tsx
import { Tile } from '@/components/Tile';
```

b) Remove the inline `Tile` definition (currently lines 14-16 in the file):

```tsx
// REMOVE:
function Tile({ children }: { children: ReactNode }) {
  return <div className="rounded-md bg-muted/40 p-4">{children}</div>;
}
```

c) Remove the now-unused `ReactNode` import if no other use remains. Check the file's `import { type ReactNode }` line — if it was only used by the inline Tile, remove it.

- [ ] **Step 2: Run client tests**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: All pass (MtdCard tests assert text/labels, not internal Tile structure).

- [ ] **Step 3: Type-check**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx
git commit -m "refactor(dashboard): use extracted Tile primitive in MtdCard"
```

---

### Task 4: Move `<CardError>` to its true home

**Files:**
- Move: `ProjectCeres.Client/src/app/features/dashboard/CardError.tsx` → `ProjectCeres.Client/src/app/components/CardError.tsx`
- Modify: 10 files that import `CardError` (paths listed below)

Importers (verified via grep at plan-write time):
- `src/app/features/dashboard/FinancialHealthCard.tsx`
- `src/app/features/dashboard/CashFlowChart.tsx`
- `src/app/features/dashboard/SpendingByCategoryChart.tsx`
- `src/app/features/dashboard/AccountBalancesChart.tsx`
- `src/app/features/dashboard/NetWorthChart.tsx`
- `src/app/features/dashboard/RemindersCard.tsx`
- `src/app/features/dashboard/MtdCard.tsx`
- `src/app/features/dashboard/IncomeExpenseChart.tsx`
- `src/app/features/dashboard/NetWorthCard.tsx`
- `src/app/pages/Movements.tsx`

The 9 dashboard-folder files currently use `'./CardError'`. The Movements page uses `'../features/dashboard/CardError'`. After the move:
- The 9 dashboard files use `'../../components/CardError'` (two `..` segments because they're inside `features/dashboard/`).
- The Movements page uses `'../components/CardError'` (one `..` segment because it's inside `pages/`).

- [ ] **Step 1: Move the source file via `git mv`**

```bash
git mv ProjectCeres.Client/src/app/features/dashboard/CardError.tsx ProjectCeres.Client/src/app/components/CardError.tsx
```

This preserves blame and makes the rename obvious in the commit log.

- [ ] **Step 2: Update imports in the 9 dashboard files**

Run this single sed command from the repo root to update all dashboard-folder imports in one shot:

```bash
sed -i '' "s|from '\./CardError'|from '../../components/CardError'|g" \
  ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx \
  ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx \
  ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx \
  ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx \
  ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx \
  ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx \
  ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx \
  ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx \
  ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx
```

(Note: the `''` argument to `-i` is for macOS sed; on Linux use `sed -i` without the `''`.)

- [ ] **Step 3: Update import in `Movements.tsx`**

```bash
sed -i '' "s|from '\.\./features/dashboard/CardError'|from '../components/CardError'|g" \
  ProjectCeres.Client/src/app/pages/Movements.tsx
```

- [ ] **Step 4: Verify all imports were updated**

Run: `grep -rn "from.*CardError" ProjectCeres.Client/src/`
Expected: All hits show the new paths (`../../components/CardError` for dashboard files, `../components/CardError` for Movements). No `'./CardError'` or `'../features/dashboard/CardError'` should remain.

- [ ] **Step 5: Type-check + run all tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No TS errors. All tests pass.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/src/app/components/CardError.tsx \
        ProjectCeres.Client/src/app/features/dashboard/CardError.tsx \
        ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx \
        ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx \
        ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx \
        ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx \
        ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx \
        ProjectCeres.Client/src/app/features/dashboard/RemindersCard.tsx \
        ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx \
        ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx \
        ProjectCeres.Client/src/app/features/dashboard/NetWorthCard.tsx \
        ProjectCeres.Client/src/app/pages/Movements.tsx
git commit -m "refactor(card-error): move CardError to src/app/components/ as a cross-feature primitive"
```

---

### Task 5: Migrate `MovementsTable` Type column to `<Badge>`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx`

- [ ] **Step 1: Replace `typeBadgeClass` lookup with Badge variant logic**

Open `ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx`. Currently the file has:

```tsx
import { Numeric } from '@/components/Numeric';
import { cn } from '@/lib/utils';
import { MovementClearedToggle } from './MovementClearedToggle';
import type { MovementListItemDto, MovementType } from './movements-api';

const typeBadgeClass: Record<MovementType, string> = {
  Transaction: 'bg-info/10 text-info',
  Transfer: 'bg-chart-4/10 text-chart-4',
  LiabilityPayment: 'bg-warning/10 text-warning',
};

const typeLabel: Record<MovementType, string> = {
  Transaction: 'Transaction',
  Transfer: 'Transfer',
  LiabilityPayment: 'Liability Payment',
};
```

Replace with:

```tsx
import { Badge } from '@/components/ui/badge';
import { Numeric } from '@/components/Numeric';
import { MovementClearedToggle } from './MovementClearedToggle';
import type { MovementListItemDto, MovementType } from './movements-api';

const typeLabel: Record<MovementType, string> = {
  Transaction: 'Transaction',
  Transfer: 'Transfer',
  LiabilityPayment: 'Liability Payment',
};
```

Drop the `cn` import if it's no longer used after the badge cell change below. Drop `typeBadgeClass` entirely.

- [ ] **Step 2: Replace the badge `<span>` in the Type column with `<Badge>`**

Find the row-level Type column rendering:

```tsx
<td className="px-3 py-2">
  <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', typeBadgeClass[item.movementType])}>
    {typeLabel[item.movementType]}
  </span>
</td>
```

Replace with:

```tsx
<td className="px-3 py-2">
  {item.movementType === 'Transaction' && (
    <Badge variant="info">{typeLabel.Transaction}</Badge>
  )}
  {item.movementType === 'Transfer' && (
    <Badge className="bg-chart-4/10 text-chart-4">{typeLabel.Transfer}</Badge>
  )}
  {item.movementType === 'LiabilityPayment' && (
    <Badge variant="warning">{typeLabel.LiabilityPayment}</Badge>
  )}
</td>
```

(The Transfer case opts out of the variant API and uses className for chart-palette tinting — this is the explicit "chart palette is not a status" deviation per the spec.)

- [ ] **Step 3: Run client tests**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: All pass — `MovementsTable.test.tsx` asserts on text content (`'Transaction'`, `'Transfer'`, `'Liability Payment'`), which is unchanged.

- [ ] **Step 4: Type-check**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementsTable.tsx
git commit -m "refactor(movements): use Badge primitive for Type column"
```

---

### Task 6: Migrate `MovementClearedToggle` to `<Badge>`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx`

- [ ] **Step 1: Replace hand-rolled badge spans with `<Badge>`**

Open `ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx`. Currently the rendering is:

```tsx
return (
  <button
    type="button"
    onClick={toggle}
    aria-label={cleared ? 'Mark as pending' : 'Mark as cleared'}
    className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
  >
    {cleared ? (
      <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-success/10 text-success">
        <CheckCircle size={12} aria-hidden="true" />
        Cleared
      </span>
    ) : (
      <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-warning/10 text-warning">
        <Clock size={12} aria-hidden="true" />
        Pending
      </span>
    )}
  </button>
);
```

Replace the `<span>` blocks with `<Badge>`:

```tsx
import { Badge } from '@/components/ui/badge';
// ... other imports unchanged ...

return (
  <button
    type="button"
    onClick={toggle}
    aria-label={cleared ? 'Mark as pending' : 'Mark as cleared'}
    className="inline-flex cursor-pointer items-center rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
  >
    {cleared ? (
      <Badge variant="success">
        <CheckCircle size={12} aria-hidden="true" />
        Cleared
      </Badge>
    ) : (
      <Badge variant="warning">
        <Clock size={12} aria-hidden="true" />
        Pending
      </Badge>
    )}
  </button>
);
```

Note: the button-level wrapper keeps focus-ring behavior but drops the redundant background/padding classes that are now on the Badge itself. The `cursor-pointer` is retained explicitly because this `<button>` doesn't go through the shadcn Button primitive (which has `cursor-pointer` baked in since v1.2).

- [ ] **Step 2: Run client tests**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: All pass — `MovementClearedToggle.test.tsx` asserts on text (`'Cleared'`, `'Pending'`) and `mockFetch` calls, which are unchanged.

- [ ] **Step 3: Type-check**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/features/movements/MovementClearedToggle.tsx
git commit -m "refactor(movements): use Badge primitive for cleared toggle"
```

---

### Task 7: Add Status Badges + Skeleton to Components showcase page

**Files:**
- Modify: `ProjectCeres.Client/src/design-system/pages/Components.tsx`

- [ ] **Step 1: Update the existing Badges section to show all 7 variants**

Find the `<h2 className="mb-4 text-xl font-medium">Badges</h2>` section. Replace its `<div className="flex flex-wrap gap-3">` content with:

```tsx
<div className="flex flex-wrap gap-3">
  <Badge>Default</Badge>
  <Badge variant="secondary">Secondary</Badge>
  <Badge variant="outline">Outline</Badge>
  <Badge variant="destructive">Destructive</Badge>
  <Badge variant="success">Success</Badge>
  <Badge variant="warning">Warning</Badge>
  <Badge variant="info">Info</Badge>
</div>
```

- [ ] **Step 2: Add a Skeleton section**

Below the existing Kbd section (around line 141), add:

```tsx
<section>
  <h2 className="mb-4 text-xl font-medium">Skeleton</h2>
  <p className="mb-4 text-sm text-muted-foreground">
    Match the rendered content's height to prevent layout shift.
    For chart cards we use 220px (<code>h-[220px]</code>).
  </p>
  <div className="flex flex-col gap-6 max-w-md">
    <div>
      <p className="mb-2 text-xs text-muted-foreground">Chart skeleton (h-[220px])</p>
      <Skeleton className="h-[220px] w-full" />
    </div>
    <div>
      <p className="mb-2 text-xs text-muted-foreground">Table skeleton (h-[400px])</p>
      <Skeleton className="h-[400px] w-full" />
    </div>
    <div>
      <p className="mb-2 text-xs text-muted-foreground">Text rows (h-5 w-32)</p>
      <div className="space-y-2">
        <Skeleton className="h-5 w-32" />
        <Skeleton className="h-5 w-32" />
        <Skeleton className="h-5 w-32" />
      </div>
    </div>
  </div>
</section>
```

Add the import at the top:

```tsx
import { Skeleton } from '@/components/ui/skeleton';
```

- [ ] **Step 3: REMOVE the existing "Stat components" section**

Find `<h2 className="mb-4 text-xl font-medium">Stat components</h2>` (around line 148) and delete the entire `<section>` containing it. This content moves to the new Patterns page in Task 9. Also drop the now-unused imports if no other section uses them:

- `Numeric`, `StatTile`, `StatRow`, `EquationRow` — check if any remaining section uses them; if not, remove the imports.

- [ ] **Step 4: Type-check + run tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No errors. All tests pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/design-system/pages/Components.tsx
git commit -m "feat(showcase): add semantic Badge variants and Skeleton; move Stat components to Patterns"
```

---

### Task 8: Create Toasts showcase page

**Files:**
- Create: `ProjectCeres.Client/src/design-system/pages/Toasts.tsx`
- Modify: `ProjectCeres.Client/src/design-system/App.tsx` — add sidebar entry + route

- [ ] **Step 1: Create the Toasts page**

Create `ProjectCeres.Client/src/design-system/pages/Toasts.tsx`:

```tsx
import { toast, Toaster } from 'sonner';
import { Button } from '@/components/ui/button';

export function Toasts() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Toasts</h1>
        <p className="mt-2 text-muted-foreground">
          Sonner is the SPA-wide toast system. A single <code>&lt;Toaster /&gt;</code> is
          mounted in <code>AppLayout.tsx</code>; this page mounts a local one for demos.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Variants</h2>
        <div className="flex flex-wrap gap-3">
          <Button onClick={() => toast.success('Saved.')}>
            Success
          </Button>
          <Button onClick={() => toast.error("Couldn't save. Try again.")}>
            Error
          </Button>
          <Button onClick={() => toast.info('Heads up.')}>
            Info
          </Button>
          <Button onClick={() => toast.warning('Watch out.')}>
            Warning
          </Button>
          <Button onClick={() => toast('Plain notification.')}>
            Plain
          </Button>
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Stacking</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Sonner queues multiple toasts. Click to fire 5 in quick succession.
        </p>
        <Button
          onClick={() => {
            for (let i = 1; i <= 5; i++) {
              setTimeout(() => toast.success(`Toast ${i} of 5`), i * 100);
            }
          }}
        >
          Fire 5 stacked toasts
        </Button>
      </section>

      <Toaster />
    </div>
  );
}
```

- [ ] **Step 2: Wire the route in `App.tsx`**

In `ProjectCeres.Client/src/design-system/App.tsx`:

a) Add the import:

```tsx
import { Toasts } from './pages/Toasts';
```

b) Add the sidebar entry (insert after `Charts` entry):

```tsx
{ to: '/charts', label: 'Charts' },
{ to: '/toasts', label: 'Toasts' },
{ to: '/components', label: 'Components' },
```

c) Add the route (insert after `/charts` route):

```tsx
<Route path="/charts" element={<Charts />} />
<Route path="/toasts" element={<Toasts />} />
<Route path="/components" element={<Components />} />
```

- [ ] **Step 3: Type-check + run tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No errors. All tests pass.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/design-system/pages/Toasts.tsx ProjectCeres.Client/src/design-system/App.tsx
git commit -m "feat(showcase): add Toasts page"
```

---

### Task 9: Create Patterns showcase page

**Files:**
- Create: `ProjectCeres.Client/src/design-system/pages/Patterns.tsx`
- Modify: `ProjectCeres.Client/src/design-system/App.tsx` — add sidebar entry + route

- [ ] **Step 1: Create the Patterns page**

Create `ProjectCeres.Client/src/design-system/pages/Patterns.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';
import { Tile } from '@/components/Tile';
import { CardError } from '@/app/components/CardError';

export function Patterns() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Patterns</h1>
        <p className="mt-2 text-muted-foreground">
          SPA-specific primitives for layout, stats, and error states.
          Use these instead of inventing one-off components.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Layout primitives</h2>
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <Card>
            <CardHeader><CardTitle>StatTile (vertical)</CardTitle></CardHeader>
            <CardContent>
              <StatTile
                label="Net Worth"
                value={<Numeric>€ 1,234.56</Numeric>}
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

          <Card>
            <CardHeader><CardTitle>Tile (KPI surface)</CardTitle></CardHeader>
            <CardContent>
              <div className="grid grid-cols-3 gap-3">
                <Tile>
                  <StatTile label="Income" value={<Numeric className="text-2xl text-success">€ 3,200</Numeric>} />
                </Tile>
                <Tile>
                  <StatTile label="Expenses" value={<Numeric className="text-2xl text-destructive">€ 1,850</Numeric>} />
                </Tile>
                <Tile>
                  <StatTile label="Savings" value={<Numeric className="text-2xl">42.2%</Numeric>} />
                </Tile>
              </div>
            </CardContent>
          </Card>
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">CardError</h2>
        <p className="mb-3 text-sm text-muted-foreground">
          Standard error+retry UI for any card whose data fetch fails.
          Pass <code>section</code> (the noun used in the error message) and
          an <code>onRetry</code> handler.
        </p>
        <Card className="max-w-md">
          <CardHeader><CardTitle>Net Worth Over Time</CardTitle></CardHeader>
          <CardContent>
            <CardError
              section="Net Worth Over Time"
              onRetry={() => alert('Retry clicked')}
            />
          </CardContent>
        </Card>
      </section>
    </div>
  );
}
```

- [ ] **Step 2: Wire the route in `App.tsx`**

In `ProjectCeres.Client/src/design-system/App.tsx`:

a) Add the import:

```tsx
import { Patterns } from './pages/Patterns';
```

b) Add the sidebar entry (insert after `Toasts` entry):

```tsx
{ to: '/toasts', label: 'Toasts' },
{ to: '/patterns', label: 'Patterns' },
{ to: '/components', label: 'Components' },
```

c) Add the route (insert after `/toasts` route):

```tsx
<Route path="/toasts" element={<Toasts />} />
<Route path="/patterns" element={<Patterns />} />
<Route path="/components" element={<Components />} />
```

- [ ] **Step 3: Type-check + run tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No errors. All tests pass.

- [ ] **Step 4: Manual smoke test**

DO NOT execute. The user runs it themselves.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/design-system/pages/Patterns.tsx ProjectCeres.Client/src/design-system/App.tsx
git commit -m "feat(showcase): add Patterns page covering layout primitives and CardError"
```

---

### Task 10: Update `docs/design-system.md`

**Files:**
- Modify: `docs/design-system.md`

- [ ] **Step 1: Read the current file**

Read `docs/design-system.md` to confirm the existing structure. The file has 11 sections (Index, How to use, Color palette, Typography, Spacing/Radius/Shadow, Motion, Chart palette, shadcn/ui overrides, Numeric component, Toasts, Showcase route, Known limitations).

This task adds three new sections (Status badges, Layout primitives, CardError), folds a Skeleton paragraph into Spacing/Radius/Shadow, and updates the shadcn/ui overrides table.

- [ ] **Step 2: Update the Index**

Find the `## Index` section. Add three new entries between the `<Numeric>` entry and the Toasts entry:

```markdown
1. [How to use this document](#how-to-use-this-document)
2. [Color palette](#color-palette)
3. [Typography](#typography)
4. [Spacing, radius, shadow](#spacing-radius-shadow)
5. [Motion](#motion)
6. [Chart palette](#chart-palette)
7. [shadcn/ui overrides](#shadcnui-overrides)
8. [The `<Numeric>` component](#the-numeric-component)
9. [Status badges](#status-badges)
10. [Layout primitives](#layout-primitives)
11. [The `<CardError>` component](#the-carderror-component)
12. [Toasts](#toasts)
13. [Showcase route](#showcase-route)
14. [Known limitations](#known-limitations)
```

- [ ] **Step 3: Add the Skeleton convention to Spacing/Radius/Shadow**

Find the `## Spacing, radius, shadow` section. Append after the existing bulleted list:

```markdown
**Skeletons:** Match the rendered content's height to prevent layout shift. Common heights: `h-[220px]` for chart cards, `h-[400px]` for tables, `h-5 w-32` for individual text rows.
```

- [ ] **Step 4: Update shadcn/ui overrides table**

Find the `## shadcn/ui overrides` section's table. Add two rows:

```markdown
| `Button cursor: pointer` | Yes | Default shadcn Button has no cursor override; we apply `cursor-pointer` so all interactive buttons get the hand cursor on hover |
| `Badge variants extended` | Yes | Added `success`, `warning`, `info` semantic variants (soft-tinted, matching the existing `destructive` recipe) |
```

- [ ] **Step 5: Add the Status badges section**

Insert a new section after the `<Numeric>` section, before Toasts:

```markdown
## Status badges

Use shadcn `<Badge>` for any small status indicator (movement type, cleared/pending state, alert tags). The semantic variants pair a tinted background with the matching foreground:

| Variant | Background | Text | Use for |
|---|---|---|---|
| `default` | primary | primary-foreground | Brand accent (rare on data tables) |
| `secondary` | secondary | secondary-foreground | Neutral metadata |
| `outline` | transparent | foreground | Quiet metadata |
| `destructive` | destructive/10 | destructive | Errors, delete-confirmation tags |
| `success` | success/10 | success | Cleared, paid, positive states |
| `warning` | warning/10 | warning | Pending, attention-needed |
| `info` | info/10 | info | Informational tags (e.g. movement type "Transaction") |

```tsx
<Badge variant="success">Cleared</Badge>
<Badge variant="warning">Pending</Badge>
<Badge variant="info">Transaction</Badge>
```

**Chart palette colors are not statuses.** When you need a non-semantic tint (e.g., the Movements page Transfer badge uses `chart-4` amber), opt out of variants and use className: `<Badge className="bg-chart-4/10 text-chart-4">Transfer</Badge>`. The explicit className signals "this is a deliberate non-semantic choice."

For destructive operations (delete confirmations), prefer a confirmation dialog over a badge.
```

- [ ] **Step 6: Add the Layout primitives section**

Insert after Status badges:

```markdown
## Layout primitives

Four small components for arranging stats and data. All live under `src/components/`.

### `<StatTile>` — vertical KPI

Label on top, value below. Use for prominent metrics that deserve visual weight.

```tsx
<StatTile label="Net Worth" value={<Numeric>€ 1,234.56</Numeric>} />
```

### `<StatRow>` — inline label/value

Label on the left, value on the right (justify-between). Use inside a `<dl className="space-y-2">` for grouped stats (MTD card, breakdown lists).

```tsx
<StatRow label="Income" value={<Numeric className="text-success">€ 3,200</Numeric>} />
```

### `<EquationRow>` — compact muted caption

Smaller (`text-[11px]`) and muted by default. Use inside dense vertical stacks for breakdowns (e.g., Spendable Balance components). Pass `valueClassName` to override the muted default for a headline row.

```tsx
<EquationRow label="Liquid" value={<Numeric>€ 1,200</Numeric>} />
<EquationRow
  label="Available today"
  value={<Numeric className="text-base font-bold text-success">€ 430</Numeric>}
/>
```

### `<Tile>` — KPI surface wrapper

Muted background + rounded + padding. Use to visually group small numeric tiles (e.g., 3-up MTD breakdown).

```tsx
<Tile>
  <StatTile label="Income" value={<Numeric className="text-2xl text-success">€ 3,200</Numeric>} />
</Tile>
```
```

- [ ] **Step 7: Add the CardError section**

Insert after Layout primitives:

```markdown
## The `<CardError>` component

`ProjectCeres.Client/src/app/components/CardError.tsx`

Standard error+retry UI for any card whose data fetch fails. Use inside `<CardContent>` when the `useApi` hook returns an error.

```tsx
{error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
```

**Props:**

- `section: string` — the noun used in the message ("Couldn't load Net Worth Over Time.").
- `onRetry: () => void` — typically the `refetch` returned by `useApi`.

The component renders a muted error line with an icon, plus an outline-variant Retry button. It does not re-fetch on its own; wire `onRetry` to your data hook.
```

- [ ] **Step 8: Verify the doc renders cleanly**

Run: `grep -c "^## " docs/design-system.md`
Expected: 14 (the 11 original sections + 3 new ones).

- [ ] **Step 9: Commit**

```bash
git add docs/design-system.md
git commit -m "docs(design-system): add status badges, layout primitives, and CardError sections"
```

---

## Self-Review Notes

**Spec coverage check:**

- §2 Architecture — covered by Tasks 1–10 collectively.
- §3a Badge variant extension — Task 1.
- §3b Tile extraction — Tasks 2 (extract) + 3 (adopt in MtdCard).
- §3c CardError move — Task 4.
- §3d Status badge migrations — Tasks 5 (MovementsTable) + 6 (MovementClearedToggle).
- §3e Markdown sections — Task 10.
- §3f New showcase pages — Tasks 8 (Toasts) + 9 (Patterns).
- §3g Existing showcase additions — Task 7.
- §4 File map — reflected in task file lists.
- §5 Testing strategy — every task includes test runs; manual verification noted in Task 9 Step 4.
- §6 Out-of-scope — explicitly listed in spec; no plan tasks.

**Type consistency check:**

- Badge variant names: `success`, `warning`, `info` consistent across Tasks 1, 5, 6, 7, 9, 10.
- Tile signature `({ children, className })` consistent across Tasks 2 (definition), 3 (consumer), 9 (showcase), 10 (docs).
- CardError import paths verified by Task 4 grep step; downstream tasks use the new path consistently.

**Placeholder scan:** None. Every code step has complete code; every test step has complete assertions.

**Path consistency check:**

- The `git mv` command in Task 4 moves CardError from `src/app/features/dashboard/CardError.tsx` to `src/app/components/CardError.tsx`. The 9 dashboard files use `'../../components/CardError'` (two `..` segments to escape both `features/dashboard/` levels). The Movements page uses `'../components/CardError'` (one `..` segment to escape `pages/`). Verified by reading the existing `Movements.tsx` import: `from '../features/dashboard/CardError'` — same one-level-up base.
