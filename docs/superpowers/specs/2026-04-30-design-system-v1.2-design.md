# Design System v1.2 — Design

## 1. Goal

Codify the component patterns that emerged during the SPA migration so they stop drifting across new features, and extend the live `/design-system.html` showcase to cover them.

## 2. Architecture

Two artifacts move together:

- **`docs/design-system.md`** — add three new sections (status badges, layout primitives, CardError), a one-line skeleton convention folded into Spacing/Radius/Shadow, and updates to the shadcn-overrides table (cursor change + new Badge variants).
- **`/design-system.html` showcase** — add a new "Toasts" page in the sidebar; add a new "Patterns" page covering SPA-specific primitives; add the new semantic Badge variants and a Skeleton example to the existing Components page.

Plus:

- **shadcn `<Badge>`** gains three variants: `success`, `warning`, `info` (`destructive` already exists).
- **`<Tile>` extracted** from the inline definition in `MtdCard.tsx` into `src/components/Tile.tsx`, re-imported.
- **`<CardError>` moved** from `src/app/features/dashboard/CardError.tsx` to `src/app/components/CardError.tsx`. Its ~13 import paths get updated.
- **All existing hand-rolled status badges migrated** to the new variants in the same slice (MovementsTable Type column, MovementClearedToggle).

Everything else (markdown sections, showcase pages) is purely additive.

## 3. Components

### 3a. shadcn `<Badge>` variant extension

`src/components/ui/badge.tsx`. Add three semantic variants to the existing `badgeVariants` cva. Pattern matches the soft-tinted recipe we've been hand-rolling: `bg-{semantic}/10 text-{semantic}`.

```tsx
const badgeVariants = cva(
  "...existing base classes...",
  {
    variants: {
      variant: {
        default: "...existing...",
        secondary: "...existing...",
        outline: "...existing...",
        destructive: "...existing (already there)...",
        // NEW:
        success: "border-transparent bg-success/10 text-success",
        warning: "border-transparent bg-warning/10 text-warning",
        info: "border-transparent bg-info/10 text-info",
      },
    },
    // ...
  }
);
```

Chart-palette colors stay opt-in via className: `<Badge className="bg-chart-4/10 text-chart-4">Transfer</Badge>`. Explicit deviation, since chart palette isn't a status.

### 3b. `<Tile>` extraction

`src/components/Tile.tsx`:

```tsx
import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

export function Tile({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('rounded-md bg-muted/40 p-4', className)}>{children}</div>;
}
```

`MtdCard.tsx` imports it instead of declaring it inline.

### 3c. `<CardError>` move

Move `src/app/features/dashboard/CardError.tsx` (and its `.test.tsx`, if present) to `src/app/components/CardError.tsx`. Contents unchanged. Use `git mv` to preserve blame.

Update imports in (final list via `grep -r "from.*CardError"` at implementation time; expect ~13 files):

- `MtdCard.tsx`, `NetWorthCard.tsx`, `RemindersCard.tsx`, `FinancialHealthCard.tsx`, `CategoryBudgetsCard.tsx`, `GoalBudgetsCard.tsx`, `KpiStrip.tsx` (or wherever the dashboard cards import it).
- All 5 chart components: `NetWorthChart`, `IncomeExpenseChart`, `SpendingByCategoryChart`, `AccountBalancesChart`, `CashFlowChart`.
- `Movements.tsx`.

### 3d. Migration of existing hand-rolled status badges

**`MovementsTable.tsx`** Type column:

- Transaction → `<Badge variant="info">Transaction</Badge>`
- Transfer → `<Badge className="bg-chart-4/10 text-chart-4">Transfer</Badge>` (chart-palette opt-out)
- LiabilityPayment → `<Badge variant="warning">Liability Payment</Badge>`

Drop the `typeBadgeClass` lookup table — the variant prop replaces it.

**`MovementClearedToggle.tsx`**:

- Cleared → `<Badge variant="success"><CheckCircle … /> Cleared</Badge>`
- Pending → `<Badge variant="warning"><Clock … /> Pending</Badge>`
- Drop the hand-rolled inline-flex + rounded-full + bg-{success|warning}/10 wrapper. The button stays as the click target; the Badge is its child.

### 3e. New markdown sections in `docs/design-system.md`

- **Status badges** (~150 words). Recipe + variant table covering the 4 default variants + the 3 new semantic. When to use which semantic. The "use chart palette via className for non-status colors" rule with the Transfer example.
- **Layout primitives** (~250 words). `<StatTile>` (vertical, label on top), `<StatRow>` (horizontal, label/value justify-between), `<EquationRow>` (compact muted caption), `<Tile>` (KPI surface wrapper). One sentence each: "use when…" + a 3-line code snippet.
- **`<CardError>`** (~80 words). Props (`section`, `onRetry`), when to use, example.
- **Skeleton convention** (~40 words). Folded into the existing Spacing/Radius/Shadow section: "Match the rendered content's height to prevent layout shift. For chart cards we use 220px (`h-[220px]`)."

Updates to existing sections:

- **shadcn/ui overrides** table: add row for `Button cursor-pointer` (deviation from default) and `Badge variants extended (success/warning/info)`.

### 3f. New showcase pages

- **`src/design-system/pages/Toasts.tsx`** — buttons for `toast.success`, `toast.error`, `toast.info`, `toast.warning` (Sonner exposes more methods than just success/error; we'll showcase what's used). Plus a "fire 5 stacked" demo button to verify queueing.
- **`src/design-system/pages/Patterns.tsx`** — sections for `<StatTile>` (with example values), `<StatRow>`, `<EquationRow>`, `<Tile>` (with stat content inside), `<CardError>` (with a working Retry button that's a no-op).

`src/design-system/App.tsx` — add 2 new sidebar entries: "Toasts" and "Patterns".

### 3g. Existing showcase additions

`src/design-system/pages/Components.tsx`:

- New `<h2>Status Badges</h2>` block showing all 7 variants (the 4 default + the 3 new semantic) in a flex row.
- New `<h2>Skeleton</h2>` block showing 3 examples: chart-tall (220px), table-tall (400px), text-rows (3× `h-5 w-32`).

## 4. File Map

**Frontend — modify:**
- `src/components/ui/badge.tsx` — extend variants
- `src/app/features/dashboard/MtdCard.tsx` — import Tile instead of declaring inline
- `src/app/features/movements/MovementsTable.tsx` — migrate Type column to `<Badge>`
- `src/app/features/movements/MovementClearedToggle.tsx` — migrate to `<Badge variant="…">`
- `src/design-system/App.tsx` — add 2 new sidebar entries
- `src/design-system/pages/Components.tsx` — add Status Badges + Skeleton sections
- ~13 files: update `import { CardError }` paths (final list via grep)

**Frontend — create:**
- `src/components/Tile.tsx` + `Tile.test.tsx`
- `src/app/components/CardError.tsx` (move target via `git mv`)
- `src/design-system/pages/Toasts.tsx`
- `src/design-system/pages/Patterns.tsx`

**Frontend — delete:**
- `src/app/features/dashboard/CardError.tsx` (moved)
- `src/app/features/dashboard/CardError.test.tsx` if it exists (moves with the source via `git mv`)

**Docs — modify:**
- `docs/design-system.md` — three new sections (Status badges, Layout primitives, CardError), Skeleton convention added to Spacing/Radius/Shadow, updates to shadcn-overrides table, Index list updated.

## 5. Testing Strategy

**Component tests:**

- `Tile.test.tsx` — renders children, applies className override.
- Existing tests for `MovementsTable`, `MovementClearedToggle` re-run unchanged — they assert text content, not class names; migration is invisible to them.
- Showcase pages (`Toasts.tsx`, `Patterns.tsx`) — no automated tests. Pure visual showcase, manually verified.
- All 13 moved-import files: `tsc --noEmit` + existing test runs cover that nothing broke.

**Manual verification (after implementation):**

- Open `/design-system.html` → Toasts page → click each button, verify Sonner toasts render with brand colors.
- Open `/design-system.html` → Patterns page → verify all 4 layout primitives + CardError render.
- Open `/design-system.html` → Components page → verify the 7 Badge variants render with proper semantic tints; verify Skeleton examples render.
- Open `/app/movements` → confirm Type column badges match the new variants visually (Transaction = info, Transfer = chart-4 amber, Liability Payment = warning).
- Confirm IsCleared toggle still works and shows Cleared/Pending badges via the new variants.

## 6. Out of Scope (logged for follow-ups)

- **Combobox extraction** (`AccountCombobox` + `CategoryCombobox` share structure) — defer until a 3rd combobox use case appears (likely Transactions full CRUD).
- **`<Field>` extraction** (the QuickAddModal helper for label + input + inline error) — defer to the Transactions/Transfers full forms slice where it'll have multiple consumers.
- **422 ValidationProblem mapping helper** — same: extract when 2+ forms need it.
- **Folder reorg** (`src/components/primitives/`, `src/components/widgets/`) — its own focused PR after v1.2, when there are enough widgets to justify the reorg.
