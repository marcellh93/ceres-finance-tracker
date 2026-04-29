# Spec: Dashboard Polish + Design System v1.1

> **Date:** 2026-04-29
> **Phase:** 3
> **Status:** Approved — ready for implementation planning
> **Predecessors:** [Dashboard Phase 1](2026-04-29-dashboard-phase-1-design.md), [App Shell](2026-04-29-app-shell-design.md), [Design System Foundation](../plans/2026-04-29-design-system-foundation.md)

A focused polish pass on the just-shipped Dashboard, plus three small reusable primitives that the rest of Phase 3 will benefit from. No new features, no new endpoints, no new pages. The goal is to bring the Dashboard from "works" to "ships" before porting the remaining Razor sections in Dashboard Phase 2.

The polish surfaces several gaps in the design system that are worth fixing now (cheaply) rather than after every page has accumulated workarounds: card chrome inconsistency, missing semantic heading on `CardTitle`, and three ad-hoc patterns ("KPI tile", "stat row", "equation row") that should be reusable primitives.

---

## Index

1. [Scope](#1-scope)
2. [New design-system primitives](#2-new-design-system-primitives)
3. [Card chrome standardization](#3-card-chrome-standardization)
4. [Inner-component refactor](#4-inner-component-refactor)
5. [Health card layout fix](#5-health-card-layout-fix)
6. [Net Worth table polish](#6-net-worth-table-polish)
7. [Reminders empty state](#7-reminders-empty-state)
8. [Showcase update](#8-showcase-update)
9. [Testing](#9-testing)
10. [Out of scope](#10-out-of-scope)

---

## 1. Scope

**In scope:**

- **Three quick fixes:**
  - Duplicate "Category Budgets" / "Goal Budgets" titles. Root cause: the inner `CategoryBudgetBars` and `GoalBudgetBars` components own their own Card chrome; the dashboard wrappers added a second Card on top.
  - Reminders empty state visual upgrade (`CheckCircle2` icon + "All caught up", success color).
  - Health card Spendable Balance equation: divider sequencing tweak so the divider above "Available today" sits flush with the headline.

- **Card chrome standardization** (touches every Card in the app):
  - `CardTitle` patched to render `<h3>` instead of `<div>` (accessibility fix, affects every Card consumer).
  - `CardTitle` size locked to `text-base font-semibold`.
  - `CardHeader` standardized to `flex items-baseline justify-between p-6 pb-4`.
  - `CardContent` standardized to `p-6 pt-0`.

- **Health card layout fix:**
  - Asymmetric grid `lg:grid-cols-[1.6fr_1fr_1fr_1fr]`. Spendable Balance gets ~36%, the three indicators ~22% each.

- **Net Worth multi-currency table polish:**
  - Zebra stripes via `even:bg-muted/30`.
  - Remove the `font-normal` overrides on `<th>` so headers read as headers.
  - Add `px-2` to cells to give zebra stripes side breathing room.

- **Three new design-system primitives** (`@/components/`):
  - `StatTile.tsx` — vertical KPI tile (small label on top, big value below).
  - `StatRow.tsx` — inline labelled value (label + value baseline-aligned).
  - `EquationRow.tsx` — compact equation row (small label left, small value right).
  - Each ~15 lines, uncoloured. Caller passes `<Numeric className=...>` for value formatting and color.

- **Retrofit dashboard cards to use the new primitives:**
  - `MtdCard` → `<StatRow>` (Income / Expenses / Savings Rate).
  - `NetWorthCard` single-currency variant → `<StatRow>` (Assets / Liabilities / Net Worth).
  - `FinancialHealthCard` Spendable Balance equation → `<EquationRow>` (deduction rows).

- **Showcase update:**
  - Add a "Stat Components" section to `Components.tsx` demoing all three primitives in their typical use.

**Out of scope:**

- New `DataTable` primitive — deferred until the Transactions page actually needs one.
- Top-bar button color rebalancing — cosmetic, tune anytime.
- Coloring deductions in the equation rows — intentional decision; deductions are neutral information, only result rows ("Available today", "Safe to spend") carry color.
- Razor `Index.cshtml` visual fixup after the inner-component refactor — accept the small Razor regression since the view is being deleted in Dashboard Phase 2.
- Dashboard Phase 2 work (the 6 remaining chart sections + Razor delete + 302 redirect).

---

## 2. New design-system primitives

All three live in `@/components/` alongside `Numeric.tsx`. Each is a presentational component that takes a label + value and applies a layout. Color, weight, and the value's mono treatment are caller's responsibility.

### `StatTile` — vertical KPI tile

Small uppercase label on top; big number below. For dashboards where each metric gets its own slot.

```tsx
type StatTileProps = {
  label: string;
  value: ReactNode;
  /** Optional class for the value element (color, weight). */
  valueClassName?: string;
};
```

Rendered structure:
```
┌──────────────────────┐
│ TOTAL SPENT          │   text-xs uppercase tracking-wider text-muted-foreground font-medium
│ € 1,234.56           │   valueClassName-controlled
└──────────────────────┘
```

Layout: `flex flex-col gap-1.5`.

### `StatRow` — inline labelled value

Label on left, value on right, baseline-aligned. For grouped stat lists.

```tsx
type StatRowProps = {
  label: string;
  value: ReactNode;
  valueClassName?: string;
};
```

Rendered structure:
```
Income                                   € 3,200.00
Expenses                                 € 1,850.45
Savings Rate                                  42.2%
```

Layout: `flex items-baseline justify-between text-sm`. Label: `text-muted-foreground`.

Renders as a `<div>` by default. Multiple `StatRow`s are typically wrapped in a `<dl className="space-y-2">` by the caller; `StatRow` itself stays neutral so it can sit inside any container.

### `EquationRow` — compact equation row

Smaller version of `StatRow`, used inside the Spendable Balance equation where you stack many rows tightly.

```tsx
type EquationRowProps = {
  label: string;
  value: ReactNode;
  valueClassName?: string;
};
```

Layout: `flex justify-between text-[11px] text-muted-foreground`. Both label and value are muted by default; caller can override the value class for headline rows ("Available today", "Safe to spend") which use `text-base font-bold` + a color from the helper.

### Shared conventions

- All three accept `value: ReactNode`, never `value: string`. Lets callers pass `<Numeric>...</Numeric>`, `<span>+12.3%</span>`, or anything else.
- `valueClassName` is optional. When present, applied to the wrapping element of the value.
- No `React.memo` — they're cheap to re-render.
- No tests of their own — covered transitively by the dashboard card tests after the retrofit. If the primitives broke, the existing tests would fail.

---

## 3. Card chrome standardization

One file change to `card.tsx`, one set of usage rules, then audit and fix existing call sites.

### `CardTitle` element fix

`shadcn/base-nova` currently renders `CardTitle` as `<div>`. Patch to render `<h3>`:

```tsx
// before
function CardTitle({ className, ...props }) {
  return <div data-slot="card-title" className={cn(...)} {...props} />;
}

// after
function CardTitle({ className, ...props }: React.ComponentProps<'h3'>) {
  return <h3 data-slot="card-title" className={cn('text-base font-semibold', ...)} {...props} />;
}
```

Every existing Card consumer benefits — design-system showcase, dashboard cards, top-bar dropdowns, future pages.

### Card title size + weight

Lock `CardTitle` defaults to `text-base font-semibold`. Set in the `card.tsx` patch above so callers don't need to remember.

### Type hierarchy locked

| Element | Size | Weight | Role |
|---|---|---|---|
| Page heading | `text-3xl` (30px) | `font-semibold` | Page H1, e.g. "Dashboard" |
| Card title | `text-base` (16px) | `font-semibold` | Card H3, e.g. "Financial Health" |
| Panel/section label inside card | `text-xs uppercase tracking-wider` | `font-medium` | e.g. "SPENDABLE BALANCE" |
| Body text | `text-sm` (14px) | `font-normal` | most content |
| Muted/caption | `text-xs` (12px) | `font-normal` | supporting text |

### Card padding standardization

| Slot | Default classes | Why |
|---|---|---|
| `CardHeader` | `flex items-baseline justify-between p-6 pb-4` | Title + optional "View all →" baseline-aligned; bottom padding tighter than top because content follows |
| `CardContent` | `p-6 pt-0` | Same horizontal padding as header, no top padding (header's bottom padding handles separation) |

These changes go in `card.tsx`. Callers can still override per-instance via className.

### Audit task

After the `card.tsx` patch lands, scan every existing Card usage and:
- Remove redundant `flex flex-row items-baseline justify-between` from `CardHeader` props (now the default).
- Remove any `text-xl` / `text-2xl` overrides on `CardTitle` if they're trying to compensate for the old default size.
- Verify the design-system showcase Card section still renders correctly.

Files to audit:
- `ProjectCeres.Client/src/design-system/pages/Components.tsx` (showcase Card section)
- `ProjectCeres.Client/src/app/features/dashboard/*.tsx` (every dashboard card)
- `ProjectCeres.Client/src/app/components/PagePlaceholder.tsx` (uses Card)
- `ProjectCeres.Client/src/components/CategoryBudgetBars.tsx` and `GoalBudgetBars.tsx` (refactored separately in §4 — but if they keep any Card usage anywhere, audit it)

---

## 4. Inner-component refactor

The duplicate-title bug is the symptom; the real issue is that `CategoryBudgetBars` and `GoalBudgetBars` own their own Card chrome.

### What changes

`ProjectCeres.Client/src/components/CategoryBudgetBars.tsx`:
- Drop the outer `<Card>` + `<CardHeader>` + `<CardTitle>`.
- Drop the `text-base` className override on the (now-removed) `CardTitle`.
- Keep the body — loading state, empty state, the bars list.
- Returns a bare `<div>` (or fragment) suitable for any container.

`ProjectCeres.Client/src/components/GoalBudgetBars.tsx`: same pattern.

After refactor: each component is fetch + render-a-list-of-bars. ~30 lines, no chrome assumptions.

### Dashboard wrapper

`CategoryBudgetsCard.tsx` and `GoalBudgetsCard.tsx` already provide the Card chrome with the "View all →" link. They simply stop seeing a duplicate title because the inner component no longer renders one. **No code change needed** in the wrappers; the bug fix is entirely in the inner components.

### Razor compatibility

The Razor pages mount `CategoryBudgetBars` and `GoalBudgetBars` as bare React islands at `data-react="category-budget-bars"` / `data-react="goal-budget-bars"` in `ProjectCeres/Views/Dashboard/Index.cshtml`. After the refactor, those islands render a bare body instead of a complete card.

**Decision: accept the visual regression in the Razor view.** The `Index.cshtml` page is being deleted in Dashboard Phase 2. Spending refactoring effort on its appearance is wasted. The dashboard cards at `/app/` (which is what users actually visit through the SPA) get the right look.

### Existing tests

Both `CategoryBudgetBars.test.tsx` and `GoalBudgetBars.test.tsx` exist. They likely assert on the Card / CardTitle being present. After refactor, those assertions will fail. Update them to assert behavior on the body only:
- Loading state renders something.
- Empty state renders the empty copy.
- A populated list renders one row per item.

Don't add tests; just update the existing ones to match the new shape.

---

## 5. Health card layout fix

### The problem

`lg:grid-cols-4` gives four equal columns. Spendable Balance is dense (~6 rows); Runway / Income vs Avg / Burn Rate are single values (often empty). On a 2560px screen, three of four columns look abandoned.

A small visible rendering bug also: the divider above "Available today" sits too high relative to the headline because the `border-t pt-1` sequencing leaves a gap between the divider and its row.

### Fix 1 — Asymmetric grid

In `FinancialHealthCard.tsx`:

```tsx
// before
<div className="grid grid-cols-1 lg:grid-cols-4 gap-6">

// after
<div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-[1.6fr_1fr_1fr_1fr] gap-6">
```

Three breakpoints:

| Width | Layout |
|---|---|
| `<md` (<768px) | 1 column — all four panels stacked |
| `md` (768–1023px) | 2 columns — Spendable Balance + Runway on top, Income vs Avg + Burn Rate below |
| `lg` (≥1024px) | Asymmetric 4-up: Spendable ~36%, three indicators ~22% each |

### Fix 2 — Equation row divider sequencing

In the "Available today" headline div, change `border-t border-border pt-1` to `border-t border-border mt-1 pt-2`. Same for "Safe to spend".

### Fix 3 — Spendable Balance equation uses `<EquationRow>`

After the new primitives land (§2), refactor the equation rows in `FinancialHealthCard.tsx`'s `SpendableEquation` function:

```tsx
// before
<div className="flex justify-between text-[11px] text-muted-foreground">
  <span>{label}</span>
  <Numeric className="text-[11px]">{value}</Numeric>
</div>

// after
<EquationRow label={label} value={<Numeric>{value}</Numeric>} />
```

The current local `Row` helper component inside `FinancialHealthCard.tsx` is removed entirely — `EquationRow` replaces it.

### Headline rows ("Available today", "Safe to spend")

These are not standard `EquationRow`s — they have a divider above and a larger value. Keep them as inline JSX inside `SpendableEquation`, but use `EquationRow` for their structure with a `valueClassName` override for the size + color:

```tsx
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
```

Same pattern for "Safe to spend" with `safeToSpendClass`.

### Tests

`FinancialHealthCard.test.tsx` (10 tests) should keep passing without changes — none assert on grid columns or pixel-level layout. Run them after the change to confirm.

---

## 6. Net Worth table polish

In `NetWorthCard.tsx` `MultiCurrency`:

- Add `even:bg-muted/30` on row className for zebra striping.
- Remove `font-normal` overrides on `<th>` cells (let browser default semibold come through).
- Add `px-2` to all `<td>` and `<th>` cells so zebra stripes have side breathing room.

That's it. No structural change. `NetWorthCard.test.tsx` already asserts behavior (single vs multi rendering, color on negative net worth) — none of these tweaks break those assertions.

### Single-currency variant uses `<StatRow>`

While we're in the file, retrofit the `SingleCurrency` function to use `<StatRow>`:

```tsx
function SingleCurrency({ entry }: { entry: NetWorthEntry }) {
  return (
    <dl className="space-y-2 text-sm">
      <StatRow label="Assets" value={<Numeric>{entry.currencySymbol} {entry.assets.toFixed(2)}</Numeric>} />
      <StatRow label="Liabilities" value={<Numeric>{entry.currencySymbol} {entry.liabilities.toFixed(2)}</Numeric>} />
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
```

`netWorthClass` (the local helper) stays.

---

## 7. Reminders empty state

In `RemindersCard.tsx`, replace the empty-state branch:

```tsx
// before
{data && data.remindersDueCount === 0 && (
  <p className="text-sm text-muted-foreground">No reminders due.</p>
)}

// after
{data && data.remindersDueCount === 0 && (
  <p className="flex items-center gap-2 text-sm text-foreground">
    <CheckCircle2 className="h-4 w-4 text-success" aria-hidden="true" />
    All caught up
  </p>
)}
```

Add `import { CheckCircle2 } from 'lucide-react';` at the top.

The "data" branch (count > 0) is unchanged.

### Test update

`RemindersCard.test.tsx` asserts on `'No reminders due.'`. Change to `'All caught up'`. No other change.

---

## 8. Showcase update

Add a new section to `ProjectCeres.Client/src/design-system/pages/Components.tsx`:

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

Add the imports at the top:

```tsx
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { StatRow } from '@/components/StatRow';
import { EquationRow } from '@/components/EquationRow';
```

---

## 9. Testing

### Two principles

1. **Don't add tests for the new primitives in isolation** (`StatTile`, `StatRow`, `EquationRow`). They're 15-line presentational components covered transitively by the dashboard card tests after the retrofit.

2. **Update existing tests minimally** — only where the assertion no longer matches reality.

### Tests that need updates

- `RemindersCard.test.tsx` — change `'No reminders due.'` to `'All caught up'`.
- `CategoryBudgetBars.test.tsx` — drop assertions on `CardTitle` / `Card`. Assert only on body behavior (loading, empty copy, populated list).
- `GoalBudgetBars.test.tsx` — same pattern as `CategoryBudgetBars.test.tsx`.

### Tests expected to stay green without change

- `MtdCard.test.tsx` — color-class assertions still apply (the `<Numeric className=...>` element remains the queried target after retrofit to `StatRow`).
- `NetWorthCard.test.tsx` — multi-currency table style change is cosmetic; assertions stay valid.
- `FinancialHealthCard.test.tsx` — 10 tests, all behavioural. Asymmetric-grid + EquationRow retrofit don't change rendered text or roles.
- `KpiStrip.test.tsx`, `Dashboard.test.tsx`, `App.test.tsx` — page-level smoke tests, unaffected.
- `Sidebar.test.tsx`, `MobileDrawer.test.tsx`, `AvatarMenu.test.tsx`, `PagePlaceholder.test.tsx` — these all use `Card`/`CardTitle` indirectly. The `CardTitle` element change from `<div>` to `<h3>` could break any test that does `screen.getByRole('heading', { level: ... })` queries. Run all tests after the patch to surface this.

### Tests to run after every change set

- After §3 (card.tsx patch): `pnpm --dir ProjectCeres.Client test` — full suite.
- After §4 (inner-component refactor): full suite.
- After §5 (Health card layout): `FinancialHealthCard.test.tsx` specifically + full suite.
- After §6 (Net Worth polish): `NetWorthCard.test.tsx` + full suite.
- After §7 (Reminders): `RemindersCard.test.tsx` + full suite.
- Final: full client tests + full backend tests + build.

### Manual verification

- Page heading hierarchy is visibly cleaner (h1 dominates, h3 card titles read as section headings).
- Cards have consistent internal padding regardless of content.
- Health card on a wide screen no longer feels empty in the right three panels.
- Health card divider above "Available today" sits flush with the headline.
- Net Worth multi-currency table has zebra stripes and bold headers.
- Reminders empty state shows green check + "All caught up".
- Category Budgets / Goal Budgets cards no longer have duplicate titles.
- `/design-system.html#/components` Stat Components section renders all three primitives.
- Existing Razor `/Dashboard` page renders without crash (some visual regression in the budget bars islands is acceptable; they're getting deleted in Phase 2).

---

## 10. Out of scope

These belong elsewhere or to follow-up plans:

- **`DataTable` primitive** — deferred until Transactions page needs one.
- **Top-bar button color rebalancing** — cosmetic, tune anytime.
- **Coloring deductions in equation rows** — intentional; deductions are neutral information.
- **Razor `Index.cshtml` visual fixup** — accept regression; view is being deleted.
- **Dashboard Phase 2** — 6 chart sections + Razor delete + 302 redirect.
- **Adding tests for the new primitives in isolation** — they're covered transitively; standalone tests would be tautological for 15-line presentational components.
- **Backend changes** — none in this plan; no API touched, no schema changed.
