# Stage 9 — Financial Health Metrics: Design Spec

**Date:** 2026-04-28
**Status:** Approved

---

## Overview

Stage 9 adds a Financial Health Snapshot panel to the dashboard and exposes the `ExcludeFromSpendable` flag on Account forms. All metrics are server-rendered Razor — no React, no async fetch. Four stats are computed by extending `DashboardService`.

---

## Metrics & Calculations

### 1. Spendable Balance

**Formula:** `SUM(non-excluded asset account balances) − SUM(EstimatedAmount of qualifying recurring transactions)`

**Qualifying recurring transactions:**
- `IsActive = true`
- `EstimatedAmount != null`
- `NextDueDate` falls within the current calendar month
- Linked account's `CurrencyId` matches the default currency from Settings

**Rationale for NextDueDate filter:** Once a recurring transaction is confirmed, `NextDueDate` advances to next month — it drops out of the calculation automatically. This prevents double-counting against an account balance that already reflects the confirmed payment.

Liability accounts are excluded from the asset sum regardless of `ExcludeFromSpendable` — they are never spendable.

### 2. Runway

**Formula:** `(total assets − total liabilities) ÷ avg monthly expenses (last 6 full calendar months)`

- "Last 6 full calendar months" = the 6 months prior to the current month (not including the current partial month)
- Scoped to default currency
- Returns `null` if avg monthly expenses = 0 or no expense data exists

### 3. Income vs. 6-Month Rolling Average

**Formula:**
- `rollingAverage` = average monthly income over the last 6 full calendar months
- `currentMonthIncome` = income recorded so far in the current calendar month
- `deltaPercent` = `(currentMonthIncome − rollingAverage) / rollingAverage`

Returns `null` if no prior 6-month data exists.

### 4. Budget Burn Rate

**Formula:** `SUM(actual spend this month across active CategoryBudgets) ÷ SUM(limit across those same budgets)`

- "Active CategoryBudgets" = `IsActive = true`, scoped to default currency
- Actual spend uses the same derivation as `BudgetService.GetActualSpendAsync`
- Returns `null` if no active CategoryBudgets exist

---

## Null Handling

All four metrics return nullable values from the service. In the Razor view, null renders as `—` with a `text-muted` class and an inline note "Not enough data". This is a one-time state that disappears once sufficient transaction history exists.

Zero is a valid computed value and is rendered as-is (e.g. runway of 0 months means assets = liabilities).

---

## Service Layer

**New record:**
```csharp
public record HealthSnapshotData(
    decimal? SpendableBalance,
    decimal? RunwayMonths,
    decimal? CurrentMonthIncome,
    decimal? RollingAverageIncome,
    decimal? IncomeDeltaPercent,
    decimal? BudgetBurnRate,
    string CurrencySymbol,
    string CurrencyCode);
```

**New method on `IDashboardService`:**
```csharp
Task<HealthSnapshotData> GetHealthSnapshotAsync();
```

Implemented in `DashboardService` as four private query methods composed into `GetHealthSnapshotAsync`. Uses the same `settingsService` injection already present on `DashboardService` for currency scoping.

---

## Account Form — ExcludeFromSpendable Toggle

The `ExcludeFromSpendable` column is already in the schema (Phase2Baseline migration). It needs to surface in the UI.

**Rules:**
- Shown on Account **Create** and **Edit** forms
- Visible only when account type is Asset — hidden for Liability accounts
- On **Edit**: `ViewBag.IsLiability` is already set; toggle visibility is server-rendered via `@if (ViewBag.IsLiability != true)`
- On **Create**: account type is chosen via dropdown, so visibility is controlled by JS — same pattern as the interest rate field show/hide on Edit. Listen to the AccountType dropdown `change` event; show the toggle when the selected type name is "Asset", hide otherwise
- `AccountCreateViewModel` and `AccountEditViewModel` get `bool ExcludeFromSpendable`
- `AccountsController` maps the field through create and edit actions
- At service level: if account type is Liability, `ExcludeFromSpendable` is forced to `false` regardless of submitted value

**Toggle UI pattern** (same as flip-sign toggle on Import form):
```html
<label class="toggle-label">
    <span class="toggle-text">Exclude from spendable balance</span>
    <span class="relative inline-block w-11 h-6">
        <input asp-for="ExcludeFromSpendable" type="checkbox"
               class="peer absolute opacity-0 w-0 h-0" />
        <span class="absolute inset-0 rounded-full bg-gray-200 transition-colors duration-200
                     peer-checked:bg-[#248e38] peer-focus:ring-2 peer-focus:ring-[#248e38]
                     peer-focus:ring-offset-1 cursor-pointer"></span>
        <span class="absolute top-0.5 left-0.5 h-5 w-5 rounded-full bg-white shadow
                     transition-transform duration-200 peer-checked:translate-x-5 cursor-pointer"></span>
    </span>
</label>
<small class="form-hint">Excluded accounts don't count toward your spendable balance but still appear in net worth.</small>
```

Inline HTML only — no `@apply`.

---

## Dashboard View

A new `_HealthSnapshot.cshtml` partial is added to `Views/Dashboard/`. It is included in `Dashboard/Index.cshtml` between the top stat cards and the React chart section.

**Panel layout:** `dashboard-card` with a `<dl class="stat-list">` containing four rows.

| Stat | Null display | Color logic |
|------|-------------|-------------|
| Spendable Balance | `—` | Green if ≥ 0, red if < 0 |
| Runway | `—` | Green > 6 months, amber 3–6 months, red < 3 months |
| Income vs. avg | `—` | Green if delta > 0, red if delta < 0, gray if 0 |
| Budget Burn Rate | `—` | Green < 50%, amber 50–80%, red > 80% |

`DashboardController` calls `GetHealthSnapshotAsync()` alongside the existing `GetDashboardDataAsync()` and passes the result to the view via `ViewBag.HealthSnapshot`.

---

## TDD

All tests written before implementation. Tests fail because the implementation does not exist.

**`DashboardServiceTests.cs`** (8 new methods):
1. Spendable balance — non-excluded asset + excluded asset + recurring transaction due this month → correct net value
2. Spendable balance — recurring transaction `NextDueDate` already in next month (confirmed) → not subtracted
3. Runway — seed 6 months of known expenses + known asset/liability balances → assert exact decimal value
4. Runway — no expense transactions in last 6 months → returns null
5. Income vs. rolling average — seed 6 months income + current month income → assert correct delta percent
6. Income vs. rolling average — no prior month income → returns null
7. Budget burn rate — seed active category budgets + current month spend → assert correct percentage
8. Budget burn rate — no active category budgets → returns null

**`AccountServiceTests.cs`** (2 new methods):
9. Create Asset account with `ExcludeFromSpendable = true` → field persisted as true
10. Create Liability account with `ExcludeFromSpendable = true` → field stored as false (service forces it)

---

## Files Changed

| File | Change |
|------|--------|
| `ProjectCeres/Services/IDashboardService.cs` | Add `HealthSnapshotData` record + `GetHealthSnapshotAsync()` |
| `ProjectCeres/Services/DashboardService.cs` | Implement four health methods |
| `ProjectCeres/Controllers/DashboardController.cs` | Call `GetHealthSnapshotAsync()`, pass via `ViewBag.HealthSnapshot` |
| `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml` | New partial — four stat rows with color indicators |
| `ProjectCeres/Views/Dashboard/Index.cshtml` | Include `_HealthSnapshot` partial |
| `ProjectCeres/ViewModels/AccountCreateViewModel.cs` | Add `bool ExcludeFromSpendable` |
| `ProjectCeres/ViewModels/AccountEditViewModel.cs` | Add `bool ExcludeFromSpendable` |
| `ProjectCeres/Controllers/AccountsController.cs` | Map `ExcludeFromSpendable` through create/edit; force false for Liability |
| `ProjectCeres/Views/Accounts/Create.cshtml` | Add toggle (Asset accounts only, via `ViewBag.IsLiability`) |
| `ProjectCeres/Views/Accounts/Edit.cshtml` | Add toggle (Asset accounts only, via `ViewBag.IsLiability`) |
| `ProjectCeres.Tests/Integration/DashboardServiceTests.cs` | 8 new test methods |
| `ProjectCeres.Tests/Integration/AccountServiceTests.cs` | 2 new test methods |
| `docs/decisions/ADR-0059-financial-health-metrics.md` | Record decisions: spendable formula, runway definition, null handling |

---

## Out of Scope

- Currency conversion — metrics are scoped to default currency only, consistent with the rest of the dashboard
- Authentication / per-user settings — Phase 3
- Storing computed health values — all derived on request, never persisted
