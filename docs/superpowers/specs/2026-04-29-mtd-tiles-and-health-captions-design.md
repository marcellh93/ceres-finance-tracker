# Spec: MTD KPI Tiles + Health Card Panel Captions

> **Date:** 2026-04-29
> **Phase:** 3
> **Status:** Approved — ready for implementation planning
> **Predecessors:** [Dashboard Polish + Design System v1.1](2026-04-29-dashboard-polish-design.md), [Dashboard Phase 1](2026-04-29-dashboard-phase-1-design.md)

A focused redesign of the **Month to Date** card and a content-density pass on three Financial Health panels. The MTD card moves from a thin `<dl>` of `StatRow`s to three horizontal KPI tiles, using the existing `StatTile` primitive on a subtle nested surface. The Financial Health panels (Burn Rate, Runway, Income vs. Avg) gain small captions under their headlines that show the underlying math — turning bare percentages into legible answers.

This is a **content density** change, not a typography or spacing change. The dashboard still uses the same palette, fonts, card chrome, and primitives. The work is: (1) MTD layout swap from rows to tiles, (2) backend extension of `HealthSnapshotData` to expose two intermediate values that today are computed and discarded, and (3) front-end renders the captions when data is present.

---

## Index

1. [Scope](#1-scope)
2. [MTD card layout](#2-mtd-card-layout)
3. [Backend extensions](#3-backend-extensions)
4. [Health card panel captions](#4-health-card-panel-captions)
5. [Testing](#5-testing)
6. [Out of scope](#6-out-of-scope)

---

## 1. Scope

**In scope:**

- **MTD card → 3 horizontal KPI tiles inside the card.** Reuses the existing `StatTile` primitive. Each tile sits in a subtle nested surface (`bg-muted/40`), padding `p-4`. Values render at `text-2xl` to anchor each tile visually. Three equal-width tiles in a `grid-cols-1 sm:grid-cols-3 gap-3`. No new design-system tokens.
- **Backend extends `HealthSnapshotData`** with three new nullable fields:
  - `AvgMonthlyExpense` — already computed in `GetRunwayAsync` and discarded; expose it.
  - `BudgetSpentMtd` — already computed in `GetBudgetBurnRateAsync` and discarded; expose it.
  - `BudgetTotalLimit` — already computed in `GetBudgetBurnRateAsync` and discarded; expose it.
- **Frontend `HealthDto`** in `api.ts` adds the three matching fields.
- **Health card panel captions** for three panels:
  - **Burn Rate:** small caption `€143 / €350 spent` underneath the headline.
  - **Runway:** small caption `at €1,000/mo` underneath the headline.
  - **Income vs. Avg:** small caption `€3,000 vs €2,700 avg` underneath the headline. Uses the *existing* `currentMonthIncome` and `rollingAverageIncome` fields — no DTO change for this panel.
- **Integration tests** — `DashboardApiHealthTests.cs` adds assertions for the three new response fields.
- **Component tests** — `FinancialHealthCard.test.tsx` adds three tests for caption rendering. Existing tests stay green.

**Out of scope:**

- Visual surface tile treatment on Financial Health panels — explicitly deferred. The user wants to evaluate the MTD tile look first, then decide whether to apply the same treatment to Health panels.
- Spendable Balance panel — already has a breakdown via the equation rows; not a "single number" panel.
- Other dashboard cards (Net Worth, Reminders, Category Budgets, Goal Budgets).
- New design-system tokens (no display typeface, no KPI-accent color). Reference image (the "Tickets Solved" mockup) was used for layout inspiration only; we adopt the spirit (surface tile + big number + caption) using our existing palette.

---

## 2. MTD card layout

### Structure

Inside the existing `<Card>` chrome (CardHeader "Month to Date", CardContent), the data branch renders three side-by-side tiles:

```
┌────────────────────────────────────────────────────────┐
│ Month to Date                                          │
│                                                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐          │
│  │ INCOME   │  │ EXPENSES │  │ SAVINGS RATE │          │
│  │ € 1,995  │  │ € 1,096  │  │   45.0%      │          │
│  └──────────┘  └──────────┘  └──────────────┘          │
└────────────────────────────────────────────────────────┘
```

### Implementation

```tsx
<dl className="grid grid-cols-1 sm:grid-cols-3 gap-3">
  <Tile>
    <StatTile
      label="Income"
      value={
        <Numeric className="text-2xl text-success">
          {sym} {data.mtd.income.toFixed(2)}
        </Numeric>
      }
    />
  </Tile>
  <Tile>
    <StatTile
      label="Expenses"
      value={
        <Numeric className="text-2xl text-destructive">
          {sym} {data.mtd.expenses.toFixed(2)}
        </Numeric>
      }
    />
  </Tile>
  <Tile>
    <StatTile
      label="Savings Rate"
      value={<Numeric className="text-2xl">{formatPercent(data.mtd.savingsRate)}</Numeric>}
    />
  </Tile>
</dl>

function Tile({ children }: { children: ReactNode }) {
  return <div className="rounded-md bg-muted/40 p-4">{children}</div>;
}
```

`<Tile>` is defined inline in `MtdCard.tsx` — a 5-line presentational wrapper. Not extracted to its own file because (a) only this card uses it for now and (b) it has no logic to test in isolation. If a second consumer appears later it can promote to `@/components/`.

### Design choices

- **Tile surface — `bg-muted/40`.** Subtle nested surface using the existing `--muted` token at ~40% opacity. Reads as "this is an object" without competing with the card's own background. Works in both light and dark via the existing token (no new tokens introduced).
- **Left-aligned content (NOT centered).** `StatTile` already renders `flex flex-col gap-1.5` (left-aligned). Currency values with symbols read more naturally left-aligned; centering adds visual fanciness without helping readability for short labels and numeric data.
- **Value size: `text-2xl`.** Big enough to anchor each tile and make the card feel content-rich, small enough to stay below the page H1 (`text-3xl`) and the card title (`text-lg`). No new typography decisions.
- **Padding: `p-4`.** Tile content sits comfortably inside its surface without crowding.
- **Gap between tiles: `gap-3`.** 12px — tighter than the typical card-to-card `gap-6`, because tiles are *inside* a card and shouldn't feel separated.
- **Stacking on mobile.** `grid-cols-1 sm:grid-cols-3` — single column under 640px, 3-up at and above. Each tile keeps its surface and padding when stacked.

### Empty and error states unchanged

- Loading: existing 3 skeleton lines.
- Error: existing `<CardError>` with retry.
- Empty (income and expenses both zero): existing muted "No transactions this month yet." copy. **NOT tiled** — empty states stay simple.

The new tile structure only applies in the data branch.

---

## 3. Backend extensions

### `HealthSnapshotData` record

`ProjectCeres/Services/IDashboardService.cs`:

```csharp
public record HealthSnapshotData(
    decimal? AvailableToday,
    decimal? SafeToSpend,
    decimal? ImminentBills,
    decimal? LaterBills,
    decimal? BudgetReserve,
    decimal? RunwayMonths,
    decimal? AvgMonthlyExpense,        // NEW
    decimal? CurrentMonthIncome,
    decimal? RollingAverageIncome,
    decimal? IncomeDeltaPercent,
    decimal? BudgetBurnRate,
    decimal? BudgetSpentMtd,           // NEW
    decimal? BudgetTotalLimit,         // NEW
    string CurrencySymbol,
    string CurrencyCode);
```

15 fields total (was 12). All three new fields are nullable; they're null whenever the related calculation can't be performed (no asset accounts, no active budgets, etc.).

### Service refactor — `GetRunwayAsync`

Currently returns `Task<decimal?>` (just the runway months). It already computes the average monthly expense internally to derive the runway. Refactor to return both:

```csharp
private async Task<(decimal? months, decimal? avgMonthlyExpense)> GetRunwayAsync(int currencyId)
```

Logic stays identical. The two return values come out of the same calculation path. When runway is null (no asset accounts), `avgMonthlyExpense` is also null.

`GetHealthSnapshotAsync` updates its call site:

```csharp
var (runway, avgMonthlyExpense) = await GetRunwayAsync(currencyId);
```

### Service refactor — `GetBudgetBurnRateAsync`

Currently returns `Task<decimal?>` (just the burn rate fraction). It already computes `actualSpend` and `totalLimit` internally. Refactor to return all three:

```csharp
private async Task<(decimal? burnRate, decimal? spent, decimal? totalLimit)> GetBudgetBurnRateAsync(int currencyId)
```

Logic stays identical. When `burnRate` is null (no active budgets, or `totalLimit == 0`), all three are null.

`GetHealthSnapshotAsync` call site:

```csharp
var (burnRate, budgetSpent, budgetTotal) = await GetBudgetBurnRateAsync(currencyId);
```

### `GetHealthSnapshotAsync` plug-in point

After both refactors, the final record construction becomes:

```csharp
return new HealthSnapshotData(
    AvailableToday:       availableToday,
    SafeToSpend:          safeToSpend,
    ImminentBills:        imminentBills,
    LaterBills:           laterBills,
    BudgetReserve:        budgetReserve,
    RunwayMonths:         runway,
    AvgMonthlyExpense:    avgMonthlyExpense,
    CurrentMonthIncome:   incomeMetrics.currentMonth,
    RollingAverageIncome: incomeMetrics.rollingAverage,
    IncomeDeltaPercent:   incomeMetrics.deltaPercent,
    BudgetBurnRate:       burnRate,
    BudgetSpentMtd:       budgetSpent,
    BudgetTotalLimit:     budgetTotal,
    CurrencySymbol:       currency.Symbol,
    CurrencyCode:         currency.Code);
```

Note: positional argument order matters because `HealthSnapshotData` is a positional record. The new fields slot in alongside their semantic neighbors (AvgMonthlyExpense after RunwayMonths; BudgetSpentMtd + BudgetTotalLimit after BudgetBurnRate).

### Frontend `HealthDto`

`ProjectCeres.Client/src/app/features/dashboard/api.ts`:

```ts
export type HealthDto = {
  availableToday: number | null;
  safeToSpend: number | null;
  imminentBills: number | null;
  laterBills: number | null;
  budgetReserve: number | null;
  runwayMonths: number | null;
  avgMonthlyExpense: number | null;     // NEW
  currentMonthIncome: number | null;
  rollingAverageIncome: number | null;
  incomeDeltaPercent: number | null;
  budgetBurnRate: number | null;
  budgetSpentMtd: number | null;        // NEW
  budgetTotalLimit: number | null;      // NEW
  currencyCode: string;
  currencySymbol: string;
};
```

Field names match the camelCase JSON output of the C# record automatically.

### Integration test

`ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs` — extend the existing `GetHealth_Returns200_WithExpectedShape` test's `requiredFields` array to include the three new fields:

```csharp
var requiredFields = new[]
{
    "availableToday", "safeToSpend", "imminentBills", "laterBills",
    "budgetReserve", "runwayMonths", "avgMonthlyExpense",
    "currentMonthIncome", "rollingAverageIncome", "incomeDeltaPercent",
    "budgetBurnRate", "budgetSpentMtd", "budgetTotalLimit",
    "currencyCode", "currencySymbol",
};
```

15 required fields. The `currencyCode`/`currencySymbol` non-null-string assertion stays unchanged.

### `api-contract.md` update

Bump the dashboard health endpoint description to mention the three new fields:

| Endpoint | Method | Path | Description |
|---|---|---|---|
| Dashboard health | GET | `/api/dashboard/health` | 15 fields, every numeric nullable: spendable balance components, runway months + avg monthly expense, current and rolling-average income, income delta percent, budget burn rate + spent + total limit, currency code/symbol |

---

## 4. Health card panel captions

Three of the four Health card panels gain a small caption beneath the headline value. Caption stays muted and small; format mimics the math being done.

### Burn Rate panel

Inside `BurnRatePanel` in `FinancialHealthCard.tsx`:

```tsx
function BurnRatePanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Budget Burn Rate</PanelLabel>
      {data.budgetBurnRate === null ? (
        <PanelEmpty>No active category budgets</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${burnRateClass(data.budgetBurnRate)}`}>
            {(data.budgetBurnRate * 100).toFixed(1)}%
          </Numeric>
          {data.budgetSpentMtd !== null && data.budgetTotalLimit !== null && (
            <div className="mt-1 text-xs text-muted-foreground">
              <Numeric>{sym} {data.budgetSpentMtd.toFixed(0)}</Numeric>
              {' / '}
              <Numeric>{sym} {data.budgetTotalLimit.toFixed(0)}</Numeric>
              {' spent'}
            </div>
          )}
        </>
      )}
    </div>
  );
}
```

Renders:
```
41.0%
€143 / €350 spent
```

### Runway panel

```tsx
function RunwayPanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Runway</PanelLabel>
      {data.runwayMonths === null ? (
        <PanelEmpty>Needs 6 months of expense history</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${runwayClass(data.runwayMonths)}`}>
            {data.runwayMonths.toFixed(1)} mo
          </Numeric>
          {data.avgMonthlyExpense !== null && (
            <div className="mt-1 text-xs text-muted-foreground">
              {'at '}
              <Numeric>{sym} {data.avgMonthlyExpense.toFixed(0)}/mo</Numeric>
            </div>
          )}
        </>
      )}
    </div>
  );
}
```

Renders:
```
8.5 mo
at €1,000/mo
```

### Income vs. Avg panel

Uses *existing* `currentMonthIncome` and `rollingAverageIncome` fields. No DTO change.

```tsx
function IncomeDeltaPanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Income vs. Avg</PanelLabel>
      {data.incomeDeltaPercent === null ? (
        <PanelEmpty>Needs 6 months of income history</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${incomeDeltaClass(data.incomeDeltaPercent)}`}>
            {data.incomeDeltaPercent >= 0 ? '+' : ''}{(data.incomeDeltaPercent * 100).toFixed(1)}%
          </Numeric>
          {data.currentMonthIncome !== null && data.rollingAverageIncome !== null && (
            <div className="mt-1 text-xs text-muted-foreground">
              <Numeric>{sym} {data.currentMonthIncome.toFixed(0)}</Numeric>
              {' vs '}
              <Numeric>{sym} {data.rollingAverageIncome.toFixed(0)}</Numeric>
              {' avg'}
            </div>
          )}
        </>
      )}
    </div>
  );
}
```

Renders:
```
+12.3%
€3,000 vs €2,700 avg
```

### Conventions across all three panels

- Caption is `text-xs text-muted-foreground` with `mt-1` separation from the headline.
- Currency symbol pulled from `data.currencySymbol` (introduce a local `const sym = data.currencySymbol` at the top of each panel for readability).
- Amounts use `<Numeric>` (mono, tabular) at the inherited small text size — no special className.
- `.toFixed(0)` for caption amounts (whole-currency-unit precision is enough at this small font).
- Caption only renders when its required fields are non-null. The headline already only renders when the panel's primary field is non-null, so caption availability typically matches.
- **Spendable Balance panel: no change.** Already has a full breakdown via equation rows.

---

## 5. Testing

### Backend integration test

`ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs`:

- Extend the `requiredFields` array in `GetHealth_Returns200_WithExpectedShape` from 12 entries to 15 by adding `avgMonthlyExpense`, `budgetSpentMtd`, `budgetTotalLimit`. Test logic is unchanged — every field name is checked the same way.
- The `GetHealth_CurrencyCodeAndSymbol_AreNonNullStrings` test stays unchanged.

### Frontend tests

**`MtdCard.test.tsx`** — existing 4 tests. Expected to keep passing:
- Income/Expenses color assertions: still find the `<Numeric className="text-success">` and `<Numeric className="text-destructive">` elements via `screen.findByText(/3200/)`. The DOM wrapping changes from `<dl>` of StatRows to `<div>` grid of Tile-wrapped StatTiles, but the `<Numeric>` elements are still queryable by their text.
- Savings rate format: `42.2%` still rendered the same way.
- Empty state: copy unchanged.
- Error/retry: behavior unchanged.

If any test fails, it's likely because an assertion is too DOM-structural rather than text-based — fix by tightening the query, not the implementation.

**`FinancialHealthCard.test.tsx`** — existing 10 tests stay green. Three new tests added:

- `Burn Rate panel shows spent/total caption when both fields are present`: mock health with `budgetBurnRate: 0.41, budgetSpentMtd: 143, budgetTotalLimit: 350`. Assert `screen.findByText(/143/)` and `findByText(/350/)` and `findByText(/spent/i)`.
- `Runway panel shows avg-monthly-expense caption when present`: mock with `runwayMonths: 8.5, avgMonthlyExpense: 1000`. Assert `findByText(/1000/)` and `findByText(/\/mo/)`.
- `Income vs. Avg panel shows current/rolling-avg caption when both fields are present`: mock with `incomeDeltaPercent: 0.111, currentMonthIncome: 3000, rollingAverageIncome: 2700`. Assert `findByText(/3000/)`, `findByText(/2700/)`, `findByText(/avg/i)`.

The "caption hides when fields null" cases are covered transitively: when the panel's primary value is null, the empty-state copy renders and the caption code path is never reached. The existing empty-state tests cover this.

### No tests for the inline `<Tile>` wrapper

Five-line presentational `<div>`. Covered transitively by `MtdCard.test.tsx`. Adding a standalone test would be tautological.

### Manual verification

- MTD card visibly displays 3 horizontal tiles with subtle muted surface, big values, all sized equally on wide screens, stacked on narrow.
- Light + dark mode both render the tile surface readably (the `bg-muted/40` token works for both).
- Burn Rate panel: shows the small caption underneath when there's data; shows "No active category budgets" empty state when not.
- Runway panel: "8.5 mo" + "at €1,000/mo" caption when data; empty-state copy when not.
- Income vs. Avg panel: "+12.3%" + "€3,000 vs €2,700 avg" caption when data; empty-state copy when not.
- Spendable Balance panel unchanged (already has its breakdown).

---

## 6. Out of scope

- **Visual surface tile treatment on Financial Health panels.** Deferred — user wants to evaluate the MTD tile look first, then decide whether to apply the same treatment to Health panels.
- **Spendable Balance panel.** Already dense via equation rows; no caption needed.
- **Other dashboard cards** (Net Worth, Reminders, Category Budgets, Goal Budgets) — out of scope for this redesign.
- **New design-system tokens.** No display typeface, no KPI-accent color introduced. The reference image was used for layout inspiration (tile + label + big number + caption); we adopt the spirit using existing tokens (Inter, IBM Plex Mono via `<Numeric>`, `--muted`, semantic colors).
- **Promoting `<Tile>` wrapper to `@/components/`.** Stays inline in `MtdCard.tsx` until a second consumer needs it.
- **Frontend caption refactor into a shared `<PanelCaption>` component.** Each panel's caption has slightly different markup ("X / Y spent" vs "at X/mo" vs "X vs Y avg") — extracting a single primitive would require either an ugly `format` enum or three different sub-components. Inline JSX is clearer at this scale.
- **Localization of caption text** ("spent" / "at" / "vs" / "avg") — handled when the localization plan lands.
- **Number formatting via Intl.NumberFormat** for thousands separators in captions. Currently `toFixed(0)` produces `1000` not `1,000`. Locale-aware formatting is part of the localization plan.
