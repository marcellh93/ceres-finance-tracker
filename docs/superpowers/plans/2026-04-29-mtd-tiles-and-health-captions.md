# MTD KPI Tiles + Health Card Panel Captions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the MTD card as 3 horizontal KPI tiles using the existing `StatTile` primitive on a subtle nested surface, and add small contextual captions under three Financial Health panel headlines (Burn Rate, Runway, Income vs. Avg) so each percentage answers "where does this number come from?".

**Architecture:** Two backend service methods (`GetRunwayAsync`, `GetBudgetBurnRateAsync`) gain richer return tuples that surface intermediate values they already compute. `HealthSnapshotData` record grows from 12 to 15 fields. The frontend `HealthDto` mirrors the new shape. Three Health panels gain caption rendering when the corresponding fields are non-null. The MTD card swaps its `<dl>` of `StatRow`s for a `grid-cols-3` of inline-defined `<Tile>` wrappers around `<StatTile>` instances. No new design-system tokens.

**Tech Stack:** ASP.NET Core (C# records, integration tests via xUnit + FluentAssertions), React 19 + TypeScript, Tailwind v4, Vitest + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-04-29-mtd-tiles-and-health-captions-design.md`. Read it before starting any task.

**Scope boundary:** No visual surface tile treatment on Health panels (deferred — user wants to evaluate MTD first). No changes to other dashboard cards. No new design-system tokens (no display typeface, no KPI accent color). The reference image was for layout inspiration only.

---

## File Structure

**Modified (server):**
- `ProjectCeres/Services/IDashboardService.cs` — `HealthSnapshotData` record adds 3 new nullable decimal fields (`AvgMonthlyExpense`, `BudgetSpentMtd`, `BudgetTotalLimit`).
- `ProjectCeres/Services/DashboardService.cs` — `GetRunwayAsync` returns `(decimal?, decimal?)` instead of `decimal?`; `GetBudgetBurnRateAsync` returns `(decimal?, decimal?, decimal?)` instead of `decimal?`; `GetHealthSnapshotAsync` plugs the new values into the record.
- `ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs` — extends the `requiredFields` array from 12 to 15.
- `docs/api-contract.md` — bump the dashboard health endpoint description.

**Modified (React client):**
- `ProjectCeres.Client/src/app/features/dashboard/api.ts` — `HealthDto` adds 3 new nullable numeric fields.
- `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx` — data branch renders 3 `<Tile>`-wrapped `<StatTile>`s instead of 3 `<StatRow>`s; defines an inline `<Tile>` wrapper.
- `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx` — `BurnRatePanel`, `RunwayPanel`, `IncomeDeltaPanel` each gain a small caption beneath the headline value when the corresponding fields are non-null.
- `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.test.tsx` — adds 3 new tests (one per caption).

---

## Task 1: Extend `HealthSnapshotData` with 3 new fields

**Files:**
- Modify: `ProjectCeres/Services/IDashboardService.cs`

The record is positional; field order matters. New fields slot in next to their semantic neighbors.

- [ ] **Step 1: Read the current record**

```bash
grep -A 16 'public record HealthSnapshotData' ProjectCeres/Services/IDashboardService.cs
```

Confirm the current shape:

```csharp
public record HealthSnapshotData(
    decimal? AvailableToday,
    decimal? SafeToSpend,
    decimal? ImminentBills,
    decimal? LaterBills,
    decimal? BudgetReserve,
    decimal? RunwayMonths,
    decimal? CurrentMonthIncome,
    decimal? RollingAverageIncome,
    decimal? IncomeDeltaPercent,
    decimal? BudgetBurnRate,
    string CurrencySymbol,
    string CurrencyCode);
```

- [ ] **Step 2: Replace the record definition**

Replace the existing `public record HealthSnapshotData(...)` declaration in `ProjectCeres/Services/IDashboardService.cs` with:

```csharp
public record HealthSnapshotData(
    decimal? AvailableToday,
    decimal? SafeToSpend,
    decimal? ImminentBills,
    decimal? LaterBills,
    decimal? BudgetReserve,
    decimal? RunwayMonths,
    decimal? AvgMonthlyExpense,
    decimal? CurrentMonthIncome,
    decimal? RollingAverageIncome,
    decimal? IncomeDeltaPercent,
    decimal? BudgetBurnRate,
    decimal? BudgetSpentMtd,
    decimal? BudgetTotalLimit,
    string CurrencySymbol,
    string CurrencyCode);
```

Three new fields:
- `AvgMonthlyExpense` — slotted right after `RunwayMonths` (semantic neighbour).
- `BudgetSpentMtd` — slotted right after `BudgetBurnRate`.
- `BudgetTotalLimit` — slotted right after `BudgetSpentMtd`.

Total: 15 fields, was 12.

- [ ] **Step 3: Run `dotnet build` to surface call-site errors**

```bash
dotnet build
```

Expected: FAIL. The compiler will report errors at the call site in `DashboardService.cs` (`GetHealthSnapshotAsync` constructs the record positionally and now has the wrong number of arguments). That's expected — Tasks 2 and 3 fix the call site.

- [ ] **Step 4: Do not commit yet**

The record change is incomplete on its own — `dotnet build` is broken. Continue to Task 2 immediately.

---

## Task 2: Refactor `GetRunwayAsync` to return runway + avg monthly expense

**Files:**
- Modify: `ProjectCeres/Services/DashboardService.cs`

The method already computes `avgMonthlyExpenses` internally (variable in source). Refactor the signature to expose it.

- [ ] **Step 1: Read the current method**

```bash
sed -n '203,275p' ProjectCeres/Services/DashboardService.cs
```

Note the two early-return paths: when `expenseTransactions.Count == 0` (returns `null`), and when `avgMonthlyExpenses == 0m` (returns `null`). Both must now return `(null, null)`.

- [ ] **Step 2: Update the signature and the three return statements**

In `ProjectCeres/Services/DashboardService.cs`, change the `GetRunwayAsync` method:

(a) Update the signature from:

```csharp
private async Task<decimal?> GetRunwayAsync(int currencyId)
```

to:

```csharp
private async Task<(decimal? months, decimal? avgMonthlyExpense)> GetRunwayAsync(int currencyId)
```

(b) Update the three return statements:

- The `if (expenseTransactions.Count == 0) return null;` line becomes `if (expenseTransactions.Count == 0) return (null, null);`
- The `if (avgMonthlyExpenses == 0m) return null;` line becomes `if (avgMonthlyExpenses == 0m) return (null, null);`
- The final `return netWorth / avgMonthlyExpenses;` line becomes `return (netWorth / avgMonthlyExpenses, avgMonthlyExpenses);`

The intermediate logic and variable names (including `avgMonthlyExpenses` plural) are unchanged.

- [ ] **Step 3: Update the call site in `GetHealthSnapshotAsync`**

In the same file, find the line in `GetHealthSnapshotAsync` (around line 53) that reads:

```csharp
var runway        = await GetRunwayAsync(currencyId);
```

Change to:

```csharp
var (runway, avgMonthlyExpense) = await GetRunwayAsync(currencyId);
```

- [ ] **Step 4: Build (still expected to fail at burn rate refactor)**

```bash
dotnet build
```

Expected: FAIL. The runway refactor compiles, but `GetBudgetBurnRateAsync` is still untouched AND the record construction in `GetHealthSnapshotAsync` still passes the wrong number of args. Both fixed in Task 3.

- [ ] **Step 5: Do not commit yet**

Continue to Task 3.

---

## Task 3: Refactor `GetBudgetBurnRateAsync` and finish `GetHealthSnapshotAsync`

**Files:**
- Modify: `ProjectCeres/Services/DashboardService.cs`

- [ ] **Step 1: Read the current burn rate method**

```bash
sed -n '326,360p' ProjectCeres/Services/DashboardService.cs
```

Note the two early-return paths: when `activeBudgets.Count == 0` (returns `null`), and when `totalLimit == 0m` (returns `null`). Both must now return `(null, null, null)`.

- [ ] **Step 2: Update the burn rate signature and return statements**

In `DashboardService.cs`, change `GetBudgetBurnRateAsync`:

(a) Update the signature from:

```csharp
private async Task<decimal?> GetBudgetBurnRateAsync(int currencyId)
```

to:

```csharp
private async Task<(decimal? burnRate, decimal? spent, decimal? totalLimit)> GetBudgetBurnRateAsync(int currencyId)
```

(b) Update the three return statements:

- The `if (activeBudgets.Count == 0) return null;` line becomes `if (activeBudgets.Count == 0) return (null, null, null);`
- The `if (totalLimit == 0m) return null;` line becomes `if (totalLimit == 0m) return (null, null, null);`
- The final `return actualSpend / totalLimit;` line becomes `return (actualSpend / totalLimit, actualSpend, totalLimit);`

The intermediate logic stays unchanged.

- [ ] **Step 3: Update the call site and the record construction in `GetHealthSnapshotAsync`**

In the same file, find these lines in `GetHealthSnapshotAsync` (around lines 53–55):

```csharp
var (runway, avgMonthlyExpense) = await GetRunwayAsync(currencyId);
var incomeMetrics = await GetIncomeMetricsAsync(currencyId);
var burnRate      = await GetBudgetBurnRateAsync(currencyId);
```

Change the burn rate destructure:

```csharp
var (burnRate, budgetSpent, budgetTotal) = await GetBudgetBurnRateAsync(currencyId);
```

Then update the `return new HealthSnapshotData(...)` call. Replace the existing call with:

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

- [ ] **Step 4: Build**

```bash
dotnet build
```

Expected: PASS. Pre-existing warning in `_HealthSnapshot.cshtml` is acceptable (unrelated).

- [ ] **Step 5: Run the full backend test suite**

```bash
dotnet test
```

Expected: PASS — all existing tests still pass. The Razor `_HealthSnapshot.cshtml` partial reads the snapshot record by named property; new fields don't break it.

- [ ] **Step 6: Commit Tasks 1+2+3 together**

```bash
git add ProjectCeres/Services/IDashboardService.cs \
        ProjectCeres/Services/DashboardService.cs
git commit -m "feat(dashboard): expose avgMonthlyExpense, budgetSpentMtd, budgetTotalLimit on health snapshot"
```

---

## Task 4: Extend the integration test

**Files:**
- Modify: `ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs`

- [ ] **Step 1: Read the current test**

```bash
cat ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs
```

Find the `requiredFields` array in `GetHealth_Returns200_WithExpectedShape`. It currently has 12 entries.

- [ ] **Step 2: Update the `requiredFields` array**

Replace the existing array with:

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

15 entries. Field name order matches the JSON shape (camelCase auto-generated from the C# record).

The `foreach` loop and assertions below the array stay unchanged.

The other test (`GetHealth_CurrencyCodeAndSymbol_AreNonNullStrings`) stays unchanged.

- [ ] **Step 3: Run the integration tests**

```bash
dotnet test --filter "FullyQualifiedName~DashboardApiHealthTests"
```

Expected: PASS — both tests green. The shape assertion now requires 15 fields and the API returns all 15.

- [ ] **Step 4: Run the full backend suite**

```bash
dotnet test
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Tests/Integration/DashboardApiHealthTests.cs
git commit -m "test(dashboard-api): assert 3 new fields on /api/dashboard/health response"
```

---

## Task 5: Update `api-contract.md`

**Files:**
- Modify: `docs/api-contract.md`

- [ ] **Step 1: Find the dashboard health row**

```bash
grep -n 'Dashboard health' docs/api-contract.md
```

The row currently reads:

```markdown
| Dashboard health | GET | `/api/dashboard/health` | 12 fields, every numeric nullable: spendable balance components, runway, income delta, budget burn rate |
```

Or similar — actual wording may differ slightly. Find the row with that endpoint path.

- [ ] **Step 2: Replace the row**

Replace the row with:

```markdown
| Dashboard health | GET | `/api/dashboard/health` | 15 fields, every numeric nullable: spendable balance components, runway months + avg monthly expense, current and rolling-average income, income delta percent, budget burn rate + spent + total limit, currency code/symbol |
```

- [ ] **Step 3: Commit**

```bash
git add docs/api-contract.md
git commit -m "docs(api-contract): document 3 new fields on /api/dashboard/health"
```

---

## Task 6: Extend frontend `HealthDto`

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/api.ts`

- [ ] **Step 1: Read the current `HealthDto`**

```bash
grep -A 14 'export type HealthDto' ProjectCeres.Client/src/app/features/dashboard/api.ts
```

- [ ] **Step 2: Replace the `HealthDto` type**

In `ProjectCeres.Client/src/app/features/dashboard/api.ts`, replace the existing `HealthDto` type with:

```ts
export type HealthDto = {
  availableToday: number | null;
  safeToSpend: number | null;
  imminentBills: number | null;
  laterBills: number | null;
  budgetReserve: number | null;
  runwayMonths: number | null;
  avgMonthlyExpense: number | null;
  currentMonthIncome: number | null;
  rollingAverageIncome: number | null;
  incomeDeltaPercent: number | null;
  budgetBurnRate: number | null;
  budgetSpentMtd: number | null;
  budgetTotalLimit: number | null;
  currencyCode: string;
  currencySymbol: string;
};
```

15 fields total.

- [ ] **Step 3: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS — TypeScript compiles. No consumers reference the new fields yet, so adding them is backward-compatible.

- [ ] **Step 4: Run client tests**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — 132 tests still green. None of the existing tests assert on the new field names.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/api.ts
git commit -m "feat(dashboard): extend HealthDto with avgMonthlyExpense, budgetSpentMtd, budgetTotalLimit"
```

---

## Task 7: MTD card — 3 horizontal KPI tiles

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx`

The data branch swaps from `<dl>` of `<StatRow>`s to a `grid-cols-3` of inline `<Tile>`-wrapped `<StatTile>`s.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx
```

- [ ] **Step 2: Replace the file**

Replace the entire contents of `ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx` with:

```tsx
import { type ReactNode } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type SummaryDto } from './api';

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

function Tile({ children }: { children: ReactNode }) {
  return <div className="rounded-md bg-muted/40 p-4">{children}</div>;
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
          <dl className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <Tile>
              <StatTile
                label="Income"
                value={
                  <Numeric className="text-2xl text-success">
                    {data.mtd.currencySymbol} {data.mtd.income.toFixed(2)}
                  </Numeric>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={
                  <Numeric className="text-2xl text-destructive">
                    {data.mtd.currencySymbol} {data.mtd.expenses.toFixed(2)}
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
        )}
      </CardContent>
    </Card>
  );
}
```

Key changes from the original:
- Added `import { type ReactNode } from 'react';` and `import { StatTile } from '@/components/StatTile';`.
- Removed `import { StatRow } from '@/components/StatRow';`.
- Added an inline `<Tile>` wrapper component (`bg-muted/40 rounded-md p-4`).
- Data branch swapped from `<dl className="space-y-6">` of three `<StatRow>`s to `<dl className="grid grid-cols-1 sm:grid-cols-3 gap-3">` of three `<Tile><StatTile/></Tile>` instances.
- Each `<Numeric>` now has `text-2xl` for the bigger value.
- Loading/error/empty branches unchanged.

- [ ] **Step 3: Run the test**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/MtdCard.test.tsx
```

Expected: PASS — all 4 tests still pass. Assertions are text-based:
- The test for income color asserts `findByText(/3200/)` and checks `className.includes('text-success')` — still works since the `<Numeric>` element with `text-success` is still queryable.
- Same for `text-destructive` on expenses.
- Savings rate test asserts `findByText('42.2%')` — unchanged.
- Empty-state copy unchanged.
- Error/retry unchanged.

If any test fails because of DOM-structure assumptions, tighten the query (don't change the implementation). The new structure is `<dl> > <div.Tile> > <div.StatTile> > <div.value> > <span.Numeric>`; the value `<span>` carries the color class.

- [ ] **Step 4: Run all client tests**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — 132 tests still green.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx
git commit -m "feat(dashboard): MtdCard renders 3 KPI tiles with bg-muted/40 surface"
```

---

## Task 8: Health card panel captions

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx`

Add small captions beneath the headline values in `BurnRatePanel`, `RunwayPanel`, and `IncomeDeltaPanel`. Each caption only renders when its required fields are non-null.

- [ ] **Step 1: Read the current file**

```bash
cat ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx
```

Note the current `RunwayPanel`, `IncomeDeltaPanel`, `BurnRatePanel` definitions. Each currently renders only the `<Numeric>` headline. We're adding a `<>...</>` fragment that wraps the headline and the caption.

- [ ] **Step 2: Update `BurnRatePanel`**

Find the `BurnRatePanel` function in `FinancialHealthCard.tsx`. Replace it with:

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

Changes from the original:
- Added `const sym = data.currencySymbol;` at the top.
- Wrapped the headline `<Numeric>` and the new caption in a fragment.
- Added the conditional caption block beneath the headline.

- [ ] **Step 3: Update `RunwayPanel`**

Find `RunwayPanel`. Replace it with:

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

- [ ] **Step 4: Update `IncomeDeltaPanel`**

Find `IncomeDeltaPanel`. Replace it with:

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

- [ ] **Step 5: Verify build**

```bash
pnpm --dir ProjectCeres.Client build
```

Expected: PASS.

- [ ] **Step 6: Run existing FinancialHealthCard tests**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/FinancialHealthCard.test.tsx
```

Expected: PASS — all 10 existing tests still pass. The captions are additive; they only render when the new fields are non-null. The mock health DTO in the existing tests doesn't set `avgMonthlyExpense`, `budgetSpentMtd`, or `budgetTotalLimit`, so they default to `undefined` in the test mocks. The conditional rendering checks `!== null`, and `undefined !== null` is `true`, so... actually the captions would render with `undefined` values, which would crash on `.toFixed(0)`.

**This means the existing test mocks need updating, OR the conditionals need to check for both null and undefined.** The clean fix is updating the mocks. We do that here in step 7.

- [ ] **Step 7: Update the existing mock health DTO**

Open `ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.test.tsx` and find the `baseHealth` constant (typed `HealthDto`). It currently lists 12 fields. Update it to include the 3 new ones with sensible default values:

```tsx
const baseHealth: HealthDto = {
  availableToday: 1000,
  safeToSpend: 800,
  imminentBills: 0,
  laterBills: 0,
  budgetReserve: 0,
  runwayMonths: 8,
  avgMonthlyExpense: 1000,
  currentMonthIncome: 3000,
  rollingAverageIncome: 2700,
  incomeDeltaPercent: 0.111,
  budgetBurnRate: 0.45,
  budgetSpentMtd: 143,
  budgetTotalLimit: 350,
  currencyCode: 'EUR',
  currencySymbol: '€',
};
```

The existing 10 tests now have a complete `HealthDto`; their assertions still pass and the captions render alongside the headlines without crashing.

- [ ] **Step 8: Add 3 new tests for the captions**

Append these three `it()` blocks to the existing `describe('FinancialHealthCard', ...)` block (above its closing `});`):

```tsx
it('Burn Rate panel shows spent/total caption when both fields are present', async () => {
  (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
    mockHealth({ budgetBurnRate: 0.41, budgetSpentMtd: 143, budgetTotalLimit: 350 }),
  );
  render(<FinancialHealthCard />);
  expect(await screen.findByText(/143/)).toBeDefined();
  expect(screen.getByText(/350/)).toBeDefined();
  expect(screen.getByText(/spent/i)).toBeDefined();
});

it('Runway panel shows avg-monthly-expense caption when present', async () => {
  (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
    mockHealth({ runwayMonths: 8.5, avgMonthlyExpense: 1000 }),
  );
  render(<FinancialHealthCard />);
  expect(await screen.findByText(/1000/)).toBeDefined();
  expect(screen.getByText(/\/mo/)).toBeDefined();
});

it('Income vs. Avg panel shows current/rolling-avg caption when both fields are present', async () => {
  (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
    mockHealth({ incomeDeltaPercent: 0.111, currentMonthIncome: 3000, rollingAverageIncome: 2700 }),
  );
  render(<FinancialHealthCard />);
  expect(await screen.findByText(/3000/)).toBeDefined();
  expect(screen.getByText(/2700/)).toBeDefined();
  expect(screen.getByText(/avg/i)).toBeDefined();
});
```

- [ ] **Step 9: Run all FinancialHealthCard tests**

```bash
pnpm --dir ProjectCeres.Client test src/app/features/dashboard/FinancialHealthCard.test.tsx
```

Expected: PASS — 13 tests now (10 existing + 3 new).

- [ ] **Step 10: Run full client suite**

```bash
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — 135 tests total (132 + 3 new).

- [ ] **Step 11: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx \
        ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.test.tsx
git commit -m "feat(dashboard): show breakdown captions on Health card Runway/Income/Burn panels"
```

---

## Task 9: Final verification

- [ ] **Step 1: Full backend build + tests**

```bash
dotnet build
dotnet test
```

Expected: PASS for both. Pre-existing warning in `_HealthSnapshot.cshtml` is acceptable.

- [ ] **Step 2: Full client build + tests**

```bash
pnpm --dir ProjectCeres.Client build
pnpm --dir ProjectCeres.Client test
```

Expected: PASS — 135 client tests across 27+ files.

- [ ] **Step 3: Manual smoke test — start the app**

In one terminal: `pnpm --dir ProjectCeres.Client dev`
In another: `dotnet run --project ProjectCeres --launch-profile https`

- [ ] **Step 4: Visit `/app/` and verify**

Open `https://localhost:7081/app/` (or `http://localhost:5248/app/`) and verify:

- **MTD card:** 3 horizontal tiles, each with subtle muted surface, label uppercase on top, big `text-2xl` value below. Income success-colored, Expenses destructive-colored, Savings Rate default. Tiles size equally on wide screens; stack to single column at <640px.
- **Light + dark mode:** the `bg-muted/40` tile surface renders cleanly in both. Toggle dark mode in DevTools (`document.documentElement.classList.add('dark')`) to confirm.
- **Burn Rate panel:** when there are active category budgets, shows `41.0%` (or whatever) with small caption like `€143 / €350 spent` underneath. Empty state ("No active category budgets") unchanged when no budgets exist.
- **Runway panel:** when there are 6 months of expense history, shows `8.5 mo` with caption `at €1000/mo`. Empty state unchanged when not enough history.
- **Income vs. Avg panel:** when there are 6 months of income history, shows `+12.3%` with caption `€3000 vs €2700 avg`. Empty state unchanged otherwise.
- **Spendable Balance panel:** unchanged (still uses the equation row breakdown).
- **Other dashboard cards:** Net Worth, Reminders, Category Budgets, Goal Budgets — visually unchanged.

- [ ] **Step 5: Stop the dev servers**

Ctrl+C both. No commit needed — verification only.

---

## Out of scope

Belong elsewhere or to follow-up plans:

- **Visual surface tile treatment on Health panels** — explicitly deferred. Compare the MTD tile look first, then decide.
- **Spendable Balance panel** — already dense via equation rows.
- **Other dashboard cards** (Net Worth, Reminders, Category Budgets, Goal Budgets).
- **New design-system tokens** (display typeface, KPI accent color).
- **Promoting `<Tile>` to `@/components/`** — stays inline in `MtdCard.tsx` until a second consumer needs it.
- **Shared `<PanelCaption>` component** — each panel's caption format differs ("X / Y spent" vs "at X/mo" vs "X vs Y avg"); inline JSX is clearer at this scale.
- **Localization** of caption words ("spent", "at", "vs", "avg") — handled in the localization plan.
- **Number formatting via `Intl.NumberFormat`** for thousands separators — part of the localization plan.
