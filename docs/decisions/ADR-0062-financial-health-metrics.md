# ADR-0062 — Financial Health Metrics (Stage 9)

**Status:** Accepted — implemented 2026-04-28

---

## Context

Personal finance trackers need to distill account positions, spending patterns, and future obligations into a few high-level indicators so users can quickly assess their financial health. A user wants to know: "How much can I safely spend this month?" (spendable balance), "How long can I sustain current expenses?" (runway), "Is my income trending up or down?" (income vs. rolling average), and "Am I within my budgets?" (budget burn rate).

These metrics are derived from transaction history, account balances, and category budgets. All are computed on request — never stored as columns — which keeps the data model normalized and avoids stale state problems.

The metrics also need to handle incomplete data gracefully. A user might have only one week of transaction history, or no budgets at all. Returning a computed zero when the input is missing would be misleading. Instead, null signals "not enough data," which is honest and disappears once history accumulates.

---

## Decision

Add four financial health metrics to `IDashboardService.GetHealthSnapshotAsync()`, all computed on request, all nullable. Results are returned as a `HealthSnapshotData` record.

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

### Metric 1: Spendable Balance

**Formula:** `SUM(balances of non-excluded asset accounts) − SUM(EstimatedAmount of qualifying recurring transactions due this calendar month)`

An asset account's balance is included unless the account is flagged with `ExcludeFromSpendable = true`. Liability accounts are always excluded regardless of the flag.

A recurring transaction qualifies if:
- `IsActive = true`
- `EstimatedAmount != null`
- `NextDueDate` falls within the current calendar month
- Linked account is one of the qualifying (non-excluded) asset accounts

**Why NextDueDate filter:** Once a recurring transaction is confirmed, `NextDueDate` advances to the next month automatically. This prevents double-counting against a balance that already reflects the confirmed payment.

**Why liability accounts are always excluded:** A liability balance represents debt owed, not available funds.

**Why the ExcludeFromSpendable flag exists:** Some asset accounts (e.g., a locked savings account, a pension fund) have balances that are not accessible for day-to-day spending. The flag lets users opt those accounts out while keeping them in net worth reports.

**Returns null when:** No qualifying (non-excluded) asset accounts exist.

### Metric 2: Runway

**Formula:** `(total assets − total liabilities) ÷ average monthly expenses (last 6 full calendar months)`

Total assets = SUM of all active asset account balances.
Total liabilities = SUM of all active liability account balances.
Average monthly expenses = `SUM(Expense transactions in the 6-month window) ÷ 6`.

The 6-month window includes the six completed calendar months immediately prior to the current month. The current month is excluded because it is incomplete and would understate the average.

For example, on 2026-04-15:
- Window is 2025-10-01 through 2026-03-31.

**Why 6 full calendar months:** Provides a stable trailing window. The current partial month is excluded to avoid biasing the average toward an unrepresentative pace.

**Returns null when:** Average monthly expenses = 0 or no expense transactions exist in the 6-month window.

### Metric 3: Income vs. 6-Month Rolling Average

**Formula:**
- `rollingAverage` = average monthly income over the last 6 full calendar months
- `currentMonthIncome` = income recorded so far in the current calendar month
- `deltaPercent` = `(currentMonthIncome − rollingAverage) / rollingAverage`

All three values (`CurrentMonthIncome`, `RollingAverageIncome`, `IncomeDeltaPercent`) are returned in `HealthSnapshotData`. `IncomeDeltaPercent` is null if there is no income in the prior 6-month window.

**Why compare to rolling average:** A single month of income is noisy. The 6-month rolling average provides a stable baseline for detecting meaningful trends — a month well above average is a positive signal; well below is a warning.

**Returns null when:** No income transactions exist in the last 6 full calendar months (rollingAverage = 0, so delta is undefined).

### Metric 4: Budget Burn Rate

**Formula:** `SUM(actual spend this month across active CategoryBudgets) ÷ SUM(limit across those same budgets)`

"Active CategoryBudgets" = `IsActive = true`, scoped to default currency.

Actual spend for each budget = SUM of transactions where `t.CategoryId == budget.CategoryId && t.Account.CurrencyId == budget.CurrencyId` for the current calendar month — same derivation as `CategoryBudgetService.GetActualSpendAsync`.

**Returns null when:** No active CategoryBudgets exist for the default currency.

### Null handling

All four metrics return nullable values.

Null means "insufficient data to compute this metric" — not zero.
- Zero is a valid computed value (e.g., runway = 0 means net worth = liabilities; burn rate = 0% means no tracked spending).
- Null is returned when computation is not meaningful (no accounts, no history, no budgets).

**UI rendering:** Null renders as `—` with an inline note "Not enough data" (`text-muted` class). Zero renders as the numeric value.

**Rationale:** Forcing a fallback like 0 would be misleading. A 0% burn rate when no budgets exist looks like a goal achieved rather than a missing input. Null makes the absence of data explicit.

### Scope

All metrics are scoped to the default currency from Settings. Currency conversion is out of scope (Phase 3+).

---

## Alternatives Rejected

**Separate service interface (`IFinancialHealthService`):** A dedicated service would add indirection without benefit — these metrics are tightly coupled to the same account, transaction, and budget data that `DashboardService` already queries. Adding `GetHealthSnapshotAsync()` to the existing `IDashboardService` is simpler and avoids a proliferation of single-method services.

**Store computed metrics as columns:** Creates stale state. A user's runway changes daily; a column would require nightly recomputation. Computing on request is always current.

**Return 0 instead of null:** Misleading (see null handling rationale above).

**Use a shorter window for runway (e.g., 3 months):** Increases month-to-month volatility. 6 months balances stability with recency.

---

## Consequences

- New `Task<HealthSnapshotData> GetHealthSnapshotAsync()` method added to `IDashboardService` and implemented in `DashboardService` as four private async query methods.
- `AccountCreateViewModel` and `AccountEditViewModel` gain `bool ExcludeFromSpendable`. `AccountService` maps the field on create and edit, forcing `false` for Liability accounts.
- `DashboardController.Index()` calls both `GetDashboardDataAsync()` and `GetHealthSnapshotAsync()`, passing the snapshot via `ViewBag.HealthSnapshot`.
- New `Views/Dashboard/_HealthSnapshot.cshtml` partial renders a "Financial Health" dashboard card with four stat rows, color-coded by severity threshold.
- No migration required — `Account.ExcludeFromSpendable` was already in the schema (Phase 2 baseline migration).
