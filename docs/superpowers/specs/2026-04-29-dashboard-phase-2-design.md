# Dashboard Phase 2 — Design

## 1. Goal

Port the remaining Razor Dashboard sections (5 charts) into the SPA, refactor the chart components to SPA conventions, fix backend chart endpoints to return typed DTOs with currency context and a 12-month window for trend series, then retire the Razor Dashboard end-to-end (302 redirect → delete views/controller).

## 2. Architecture

- **Frontend.** The 5 chart components move from `src/components/` into `src/app/features/dashboard/`. Each is refactored to use `useApi`, `<CardError>`, `<Skeleton>` (220 px tall), shadcn `<CardTitle>` (h3 / semibold / text-lg) with `text-xs text-muted-foreground` subtitles where the timeframe isn't obvious. Recharts color strings come from a new `chartColors` util that returns `var(--chart-N)` plus semantic tokens (`--success` / `--destructive`) where income vs expense applies. Month labels go through a new `formatMonth(yyyyMm)` helper using `Intl.DateTimeFormat` with `{ month: 'short', year: 'numeric' }` (e.g. `"Apr 2026"`).
- **Backend.** The 5 chart endpoints are converted to typed records. Each response is wrapped with `{ currencyCode, currencySymbol, ...payload }`. Trend endpoints (`net-worth-trend`, `income-expense`, `cash-flow`) extend their window from 6 to 12 months. `spending-by-category` adds a `total` so the chart can show "% of total".
- **Razor cleanup.** A 302 redirect from `/Dashboard` → `/app/` ships first; then `Views/Dashboard/Index.cshtml`, `_HealthSnapshot.cshtml`, and `Controllers/DashboardController.cs` (the MVC one — *not* the API controller) are deleted, along with the now-unused `data-react` mounting blocks for the 5 charts in `ProjectCeres.Client/src/main.tsx`. The API controller, services, models, and chart components themselves stay (the chart components are moved to their new home).

## 3. Components

### 3a. New backend DTOs

In `ProjectCeres/ViewModels/`:

```csharp
public record NetWorthTrendPoint(string Month, decimal Assets, decimal Liabilities, decimal NetWorth);
public record NetWorthTrendDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<NetWorthTrendPoint> Points);

public record IncomeExpensePoint(string Month, decimal Income, decimal Expenses);
public record IncomeExpenseDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<IncomeExpensePoint> Points);

public record SpendingByCategorySlice(string CategoryName, decimal Amount);
public record SpendingByCategoryDto(string CurrencyCode, string CurrencySymbol, decimal Total, IReadOnlyList<SpendingByCategorySlice> Slices);

public record AccountBalanceRow(string AccountName, decimal Balance);
public record AccountBalancesDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<AccountBalanceRow> Rows);

public record CashFlowPoint(string Month, decimal NetFlow);
public record CashFlowDto(string CurrencyCode, string CurrencySymbol, IReadOnlyList<CashFlowPoint> Points);
```

`Month` stays as `"yyyy-MM"` strings (sortable, parseable, locale-independent). The frontend formats for display.

### 3b. `chartColors` util

`src/app/lib/chart-colors.ts`:

```ts
export const chartColors = {
  income: 'var(--success)',
  expense: 'var(--destructive)',
  netWorth: 'var(--chart-1)',
  assets: 'var(--chart-2)',
  liabilities: 'var(--chart-3)',
  slot: (n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8) => `var(--chart-${n})`,
};
```

Recharts accepts CSS variable strings directly for `stroke` and `fill`.

### 3c. `formatMonth` helper

`src/app/lib/format-month.ts`:

```ts
export function formatMonth(yyyyMm: string): string {
  const [y, m] = yyyyMm.split('-').map(Number);
  return new Date(y, m - 1).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
}
// "2026-04" → "Apr 2026"
```

Used in all chart `XAxis tickFormatter={formatMonth}` and tooltip `labelFormatter={formatMonth}`. Settings-aware date formatting is deferred (see §9).

### 3d. Refactored chart components

All 5 charts move to `src/app/features/dashboard/` and follow the same skeleton:

```tsx
export function NetWorthChart() {
  const { data, error, loading, refetch } = useApi<NetWorthTrendDto>('/api/dashboard/net-worth-trend');
  return (
    <Card>
      <CardHeader>
        <CardTitle>Net Worth Over Time</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
        {data && data.points.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.points.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>{/* chart here */}</ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

Per-chart specifics:

| Component | Chart type | Series / colors | Subtitle | Notes |
|---|---|---|---|---|
| `NetWorthChart` | LineChart | Assets (`chartColors.assets`), Net Worth (`chartColors.netWorth`) | "Last 12 months" | Tooltip prefixes value with `currencySymbol` |
| `IncomeExpenseChart` | BarChart | Income (`chartColors.income`), Expenses (`chartColors.expense`) | "Last 12 months" | |
| `SpendingByCategoryChart` | Horizontal BarChart | Top 10 categories, slot colors cycling | "This month" | Tooltip shows amount + `(% of total)` using `data.total` |
| `AccountBalancesChart` | Horizontal BarChart | Slot colors cycling | (none — current snapshot) | |
| `CashFlowChart` | BarChart | Single series; positive bars `--success`, negative bars `--destructive` (Recharts cell-level coloring) | "Last 12 months" | |

## 4. Layout

`pages/Dashboard.tsx` becomes:

```tsx
<div className="space-y-6">
  <h1>Dashboard</h1>

  <FinancialHealthCard />
  <KpiStrip />

  <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
    <CategoryBudgetsCard />
    <GoalBudgetsCard />
  </div>

  <NetWorthChart />
  <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
    <IncomeExpenseChart />
    <SpendingByCategoryChart />
    <AccountBalancesChart />
    <CashFlowChart />
  </div>
</div>
```

Net Worth is full-width as the headline trend; the other four charts form a uniform 2x2 below. No tabs, no action bar.

## 5. Razor Cleanup & Migration

**Remove** (Razor MVC dashboard):
- `ProjectCeres/Views/Dashboard/Index.cshtml`
- `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml`
- `ProjectCeres/Controllers/DashboardController.cs` (MVC) — *not* `Controllers/Api/DashboardApiController.cs`
- Any ViewModels used only by the MVC `DashboardController` (audit during implementation; `DashboardData` record stays — used by `IDashboardService` and the API)
- `data-react` mounting blocks for the 5 chart selectors in `ProjectCeres.Client/src/main.tsx` (`net-worth-chart`, `income-expense-chart`, `spending-by-category-chart`, `account-balances-chart`, `cash-flow-chart`) — they have no remaining mount points

**Add 302 redirect:**
- `/Dashboard` → `/app/` (and `/Dashboard/Index` for completeness). Per the per-area redirect rules in `planning-phase3-spa-migration.md`: 302 (temporary) during the migration phase. The global one-shot 301 from `/app/*` → `/*` happens later when the SPA moves to `/`.

**Keep** (still in use):
- `ProjectCeres/Controllers/Api/DashboardApiController.cs` — refactored DTOs, but keeps serving the SPA
- `ProjectCeres/Services/IDashboardService.cs` and `DashboardService.cs` — interface unchanged; only `DashboardApiController` actions adapt
- The 5 chart components — moved from `src/components/` to `src/app/features/dashboard/`

**Tests to update or remove:**
- Razor view tests, if any reference `Views/Dashboard/Index.cshtml` or `_HealthSnapshot.cshtml` — delete
- Existing chart component tests in `src/components/*Chart.test.tsx` — move alongside the components and rewrite to assert `useApi` integration + new DTO shape

## 6. Data Flow & Error Handling

- Each chart mounts → `useApi(URL)` → `{ data, error, loading, refetch }`.
- Loading → `<Skeleton className="h-[220px] w-full" />`.
- Error → `<CardError section="..." onRetry={refetch} />`.
- Empty (`data.points.length === 0` or equivalent) → muted "No data yet."
- Populated → `<ResponsiveContainer>` with the chart.
- Currency comes from `data.currencySymbol` and is used in tooltip `formatter` strings.

## 7. Testing Strategy

**Backend (xUnit + FluentAssertions):**
- Each refactored endpoint gets/keeps an integration test asserting: response is the new wrapper DTO, `currencyCode` matches default-currency from settings, `currencySymbol` is non-empty, `points`/`slices`/`rows` length and ordering. For trend endpoints, assert window length is 12 months.
- New test for the `/Dashboard` → `/app/` 302 redirect.

**Frontend (Vitest + RTL):**
- Each refactored chart: loading skeleton, error → CardError, empty state, populated state.
- `chartColors` and `formatMonth` units (stable string assertions).

## 8. Documentation Updates

- `docs/api-contract.md` — update the 5 chart endpoint rows to reflect the new wrapper shapes.
- `docs/planning-phase3-spa-migration.md` — mark Dashboard as fully migrated; note the `/Dashboard` 302 redirect is live.
- `docs/planning-phase3.md` — mark Dashboard SPA migration item complete.
- `docs/planning-future.md` — add the **Settings-aware formatting** follow-up (locale-aware dates, numbers, percentages, time zone — Razor `NumberFormatHelper` parity in SPA).

## 9. Out of Scope (logged for follow-ups)

- **Settings-aware formatting.** A `useSettings()` hook + `formatDate`/`formatMonth`/`formatNumber`/`formatPercent` utils that respect the user's `DateFormat` and `NumberFormat` preferences. Would touch every feature, not just charts; deserves its own scope.
- Renaming "Cash Flow" (currently shows net of income − expenses; the term is fine for now).
- Timeframe selector on chart cards (`?months=N` param).
- Custom tooltip components beyond default Recharts.
