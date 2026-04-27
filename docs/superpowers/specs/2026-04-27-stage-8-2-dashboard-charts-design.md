# Stage 8.2 — Dashboard Chart Components Design

**Date:** 2026-04-27
**Status:** Approved

---

## Overview

Add 5 new Recharts-based chart components to the existing dashboard page. Two progress bar components (`CategoryBudgetBars`, `GoalBudgetBars`) are already complete and untouched. The 5 new charts are added as a dedicated section below the budget bars, following the TDD pattern established in the roadmap: API endpoint test first, then component Vitest smoke test.

---

## Decisions Made

| Decision | Choice | Rationale |
|---|---|---|
| Chart library | Recharts (not Chart.js/react-chartjs-2) | shadcn/ui ships a `<ChartContainer>` built on Recharts — consistent theming, no canvas mock needed in Vitest |
| Page placement | Existing `/Dashboard` page | Charts appended below existing stats + budget bars section |
| Layout | 2-col row (Net Worth + Income vs Expenses), then 3-col row (Spending + Balances + Cash Flow) | Keeps large time-series charts together; smaller charts share a row |
| Default time window | Last 6 months (rolling from today) | Aligns with Stage 9 financial health metrics; enough data for trend visibility |
| Vitest test depth | Smoke test only — component mounts, chart container present | Data correctness covered by WebApplicationFactory tests on API endpoints |

---

## Architecture

### New API Endpoints (`DashboardApiController`)

Five new `GET` endpoints added to the existing `DashboardApiController` at `Controllers/Api/DashboardApiController.cs`:

| Endpoint | Returns | Used by |
|---|---|---|
| `GET /api/dashboard/net-worth-trend` | Monthly net worth snapshots (last 6 months) | `NetWorthChart` |
| `GET /api/dashboard/income-expense` | Monthly income + expense totals (last 6 months) | `IncomeExpenseChart` |
| `GET /api/dashboard/spending-by-category` | Per-category expense totals for current month | `SpendingDonutChart` |
| `GET /api/dashboard/account-balances` | Current balance per active account (default currency) | `AccountBalancesChart` |
| `GET /api/dashboard/cash-flow` | Monthly net cash flow = income − expenses (last 6 months) | `CashFlowChart` |

All endpoints filter by the user's default currency (read from `ISettingsService`), matching the existing dashboard convention.

**Note on `net-worth-trend` computation:** Account balance is always derived (never stored). Monthly net worth snapshots are computed by summing all transactions up to the last day of each month per account, then aggregating assets and liabilities. This is the same approach used by `ReportService.GetNetWorthAsync` — the endpoint will reuse or replicate that logic scoped to a rolling 6-month window.

#### Response shapes

```jsonc
// GET /api/dashboard/net-worth-trend
[{ "month": "2025-11", "assets": 12000.00, "liabilities": 3000.00, "netWorth": 9000.00 }]

// GET /api/dashboard/income-expense
[{ "month": "2025-11", "income": 3500.00, "expenses": 2100.00 }]

// GET /api/dashboard/spending-by-category
[{ "categoryName": "Groceries", "amount": 420.00 }]

// GET /api/dashboard/account-balances
[{ "accountName": "Checking", "balance": 4200.00 }]

// GET /api/dashboard/cash-flow
[{ "month": "2025-11", "netFlow": 1400.00 }]
```

`month` fields are `"YYYY-MM"` strings. Negative `netWorth` or `netFlow` are valid and must be handled by the chart components.

### New React Components (`ProjectCeres.Client/src/components/`)

| File | Chart type | Recharts component |
|---|---|---|
| `NetWorthChart.tsx` | Line | `<LineChart>` with two lines: Assets (blue) and Net Worth (green) |
| `IncomeExpenseChart.tsx` | Grouped bar | `<BarChart>` with two bars per month: Income (green) and Expenses (red) |
| `SpendingDonutChart.tsx` | Doughnut | `<PieChart>` with `innerRadius` set |
| `AccountBalancesChart.tsx` | Horizontal bar | `<BarChart layout="vertical">` |
| `CashFlowChart.tsx` | Bar | `<BarChart>` single bar per month; negative values render in red via `<Cell>` |

Each component:
- Fetches its own data via `fetch('/api/dashboard/...')` in `useEffect`
- Shows a loading state (`text-sm text-muted-foreground`) while fetching
- Shows an empty state if the array is empty
- Is wrapped in a shadcn/ui `<Card>` with `<CardHeader>` + `<CardTitle>`
- Uses `<ChartContainer>` from `@/components/ui/chart` for consistent theming

### Dashboard View Changes

`ProjectCeres/Views/Dashboard/Index.cshtml` gets a new section appended after the budget bars `<div>`:

```html
<!-- Row 1: time-series charts -->
<div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
  <div data-react="net-worth-chart"></div>
  <div data-react="income-expense-chart"></div>
</div>

<!-- Row 2: aggregate charts -->
<div class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
  <div data-react="spending-donut-chart"></div>
  <div data-react="account-balances-chart"></div>
  <div data-react="cash-flow-chart"></div>
</div>
```

`main.tsx` gets 5 new mount blocks following the existing `querySelector` + `createRoot` pattern.

---

## TDD Plan (per chart)

For each of the 5 charts, the order is:

1. Write `WebApplicationFactory` integration test: `GET /api/dashboard/{endpoint}` with seeded data → assert JSON shape (correct fields present, correct count).
2. Test fails (endpoint doesn't exist).
3. Implement endpoint in `DashboardApiController`.
4. Test passes.
5. Write Vitest smoke test: mock `fetch` → component renders without throwing → chart container element is present in the DOM.
6. Test fails (component doesn't exist).
7. Implement the React component.
8. Test passes.
9. Add `data-react` mount point to `Index.cshtml` and mount block to `main.tsx`.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `ProjectCeres.Client/src/components/NetWorthChart.tsx` | Line chart |
| `ProjectCeres.Client/src/components/NetWorthChart.test.tsx` | Vitest smoke test |
| `ProjectCeres.Client/src/components/IncomeExpenseChart.tsx` | Grouped bar chart |
| `ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx` | Vitest smoke test |
| `ProjectCeres.Client/src/components/SpendingDonutChart.tsx` | Doughnut chart |
| `ProjectCeres.Client/src/components/SpendingDonutChart.test.tsx` | Vitest smoke test |
| `ProjectCeres.Client/src/components/AccountBalancesChart.tsx` | Horizontal bar chart |
| `ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx` | Vitest smoke test |
| `ProjectCeres.Client/src/components/CashFlowChart.tsx` | Bar chart with negative value support |
| `ProjectCeres.Client/src/components/CashFlowChart.test.tsx` | Vitest smoke test |

### Modified files

| File | Change |
|---|---|
| `ProjectCeres/Controllers/Api/DashboardApiController.cs` | Add 5 new `GET` endpoints |
| `ProjectCeres/Views/Dashboard/Index.cshtml` | Add 2-col + 3-col chart section below budget bars |
| `ProjectCeres.Client/src/main.tsx` | Add 5 new `querySelector` + `createRoot` mount blocks |
| `ProjectCeres.Tests/Integration/DashboardApiTests.cs` | Add 5 new endpoint integration tests (or new file if it doesn't exist) |

### Dependencies to install

```bash
cd ProjectCeres.Client
pnpm add recharts
pnpm dlx shadcn add chart   # adds @/components/ui/chart (ChartContainer wrapper)
```

---

## Out of Scope

- Stage 8.1 (Razor visual migration) — separate plan
- Date range filter controls on the dashboard — Phase 3 or a future enhancement
- Currency selector on dashboard charts — charts use default currency only (existing convention)
- Account Balances chart showing liability accounts — assets only for clarity; liabilities shown in Net Worth

---

## Verification Checklist (from roadmap)

- [ ] Dashboard loads all 5 new chart components; no console errors
- [ ] Net Worth Over Time chart: points correspond to known account balances at end of each month
- [ ] Income vs. Expenses chart: bars match MTD totals shown in the text dashboard
- [ ] Spending by Category donut: slices sum to total expense amount for the current month
- [ ] Account Balances chart: all active accounts shown with correct balances (default currency)
- [ ] Monthly Cash Flow chart: net values (income − expenses) correct per month
- [ ] `dotnet test` — 0 failed after all 5 API endpoint tests added
- [ ] `pnpm test` — 0 failed after all 5 Vitest smoke tests added
