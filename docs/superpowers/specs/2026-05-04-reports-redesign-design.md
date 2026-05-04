# Reports Redesign — Design Spec

**Date:** 2026-05-04
**Status:** Approved

## Overview

Upgrade the Reports section from a plain table-per-report pattern to a premium financial reporting experience: tab bar navigation, shared filter state, KPI tiles, charts, and paginated tables. No new API endpoints required.

## 1. Navigation & Layout Shell

**Tab bar** replaces the card grid index (`ReportsIndex`). Rendered in `ReportsLayout` above the `<Outlet>`, using the existing `line` variant from `tabs.tsx`. One tab per report (8 total). Active tab gets the `after:bg-foreground` underline. Tabs scroll horizontally on mobile — no wrapping.

**Shared filter bar** renders in `ReportsLayout` directly below the tab bar, sticky at `top-14 z-10 bg-background/95 backdrop-blur`. Contains: date range (start + end month) + currency selector. These are the only two controls shared across all reports.

**Per-report filters** stay local to each report page, rendered below the shared bar via `ReportLocalFilterBar` (renamed from `ReportsFilterBar`):
- Budget vs Actual: budget selector
- Transaction History: search input + category filter
- All other reports: no local filters

**Content width:** `px-[8%]` with no `max-width` cap. Fluid — scales proportionally to any viewport. Replaces current `max-w-4xl`.

**Back button:** removed from `ReportsLayout`. Tab bar is the navigation affordance.

**Index route** (`/reports`): redirects to `/reports/net-worth-over-time` carrying default search params.

## 2. Per-Report Page Anatomy

Every report page follows a four-zone vertical stack:

### Zone 1 — Page header
`h1` report title + one-line description. Not sticky. Uses existing `ReportHeader` component (tabIndex={-1} focus on mount preserved).

### Zone 2 — KPI tiles
Three `<Tile><StatTile>` primitives in a 3-column grid. Each tile shows:
- **Value:** end-of-period figure (last row/entry in the dataset)
- **Delta:** end-of-period minus start-of-period for the selected date range, with directional color (green = positive, red = negative)
- **Label:** uppercase, tracked, muted

Transaction History skips this zone (no meaningful aggregate KPI).

### Zone 3 — Chart (6 of 8 reports)
Recharts `ChartContainer` wrapping the appropriate chart type, full content width. Chart type per report:

| Report | Chart type |
|---|---|
| Net Worth Over Time | `AreaChart` with linearGradient fill |
| Income vs Expense | `BarChart` grouped (2 bars per month) |
| Monthly Cash Flow | `BarChart` stacked |
| Expense Breakdown | `BarChart` horizontal (`layout="vertical"`) |
| Budget vs Actual | Existing `<Progress>` bars — no Recharts |
| Largest Expenses | `BarChart` horizontal |

Net Worth (snapshot) and Transaction History: no chart, table only.

### Zone 4 — Table
`ReportTableCard` with 6-row default + client-side pagination. Export CSV button stays in the table card header.

## 3. Shared Filter Bar & URL State

Filter state lives in React Router search params so filters are shareable and bookmarkable.

**Shared params (in URL, carried across tab switches):**
- `from` — start month, format `YYYY-MM` (e.g. `2026-01`)
- `to` — end month, format `YYYY-MM` (e.g. `2026-04`)
- `currency` — ISO code (e.g. `EUR`)

**Default state (no params in URL):** current month for both `from` and `to`; user's default currency from Settings.

**Tab navigation:** tab links are `<Link>` elements that carry current search params forward. Switching tabs preserves the date range and currency selection.

**Per-report local filters** are component state only — not in the URL.

## 4. Component Changes

### New components
- `ReportsTabBar` — reads `REPORT_META` for tab labels/slugs, renders tab bar, carries search params on navigation
- `ReportsSharedFilterBar` — date range pickers + currency selector; reads/writes `useSearchParams`

### Modified components
- `ReportsLayout` — adds `ReportsTabBar` + `ReportsSharedFilterBar`; removes back button; changes `max-w-4xl` to `px-[8%]`
- `ReportsFilterBar` → renamed `ReportLocalFilterBar` — stripped of date/currency controls; retains report-specific filters only
- `ReportTableCard` — adds optional `pagination` prop; if provided, renders footer with prev/next controls and page indicator; backwards compatible (omit prop = show all rows)
- Each of the 6 chart reports — adds KPI zone (Zone 2) and chart zone (Zone 3) above existing table

### Deleted
- `ReportsIndex` — card grid replaced by tab bar + redirect

### Unchanged
- `ReportHeader`
- `reports-api.ts` — no new endpoints, no DTO changes

## 5. KPI Derivation (client-side, from existing data)

All KPI values derived from data already returned by each report's existing fetch. No new API calls.

| Report | KPI 1 | KPI 2 | KPI 3 | Delta basis |
|---|---|---|---|---|
| Net Worth Over Time | End-period Net Worth | End-period Assets | End-period Liabilities | End minus start of period |
| Income vs Expense | Total Income | Total Expenses | Net (Income − Expenses) | Period sum vs prior equal-length period |
| Monthly Cash Flow | Total Income | Total Expenses | Net Flow | Period sum vs prior equal-length period |
| Expense Breakdown | Largest category (name + amount) | Total expenses | Category count | None |
| Budget vs Actual | Total budgeted | Total spent | Overall % used | None |
| Largest Expenses | Top expense (name + amount) | Top-N total | Average transaction | None |

## 6. Pagination

Client-side only. No server changes.

**`usePagination` hook** — `src/hooks/usePagination.ts`
- Input: `items: T[]`, `pageSize: number`
- Output: `{ paginatedItems, currentPage, totalPages, next, prev, goTo }`

**Behavior:**
- Default page size: 6 rows
- Controls: `←` · `Page N of M` · `→` in `ReportTableCard` footer
- Arrows disabled at boundaries
- Resets to page 1 on filter change or tab switch

**`ReportTableCard` change:**
```ts
interface ReportTableCardProps {
  // ...existing props
  pagination?: {
    currentPage: number
    totalPages: number
    onNext: () => void
    onPrev: () => void
  }
}
```
If `pagination` is omitted, all rows render as today (backwards compatible).
