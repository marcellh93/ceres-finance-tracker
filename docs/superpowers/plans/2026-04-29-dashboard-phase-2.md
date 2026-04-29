# Dashboard Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the remaining 5 Razor Dashboard charts to the SPA, refactor backend chart endpoints to typed DTOs with currency context (and 12-month windows for trend series), then retire the Razor Dashboard end-to-end via a 302 redirect and file deletion.

**Architecture:** Backend converts five anonymous-object endpoints in `DashboardApiController` to typed records wrapped in `{ currencyCode, currencySymbol, ...payload }` shapes; frontend chart components move from `src/components/` to `src/app/features/dashboard/` and adopt the SPA dashboard pattern (`useApi` + `<CardError>` + `<Skeleton>` + shadcn `<CardTitle>` h3). Razor cleanup follows: 302 redirect from `/Dashboard` → `/app/`, then delete the MVC dashboard view, partial, and controller.

**Tech Stack:** ASP.NET Core MVC + EF Core + xUnit/FluentAssertions on the server; React 19 + TypeScript + Vite + Recharts + Tailwind v4 + shadcn/ui (`base-nova`) + Vitest/RTL on the client.

**Spec:** `docs/superpowers/specs/2026-04-29-dashboard-phase-2-design.md`

---

## File Map

**Backend — create:**
- `ProjectCeres/ViewModels/DashboardChartDtos.cs` — all 5 chart DTOs in one file (related types, all small)

**Backend — modify:**
- `ProjectCeres/Controllers/Api/DashboardApiController.cs` — refactor 5 chart actions to return new DTOs; trend windows 6 → 12 months
- `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — update assertions for new wrapper shapes & 12-month window
- `ProjectCeres/Controllers/DashboardController.cs` — replace `Index()` with `RedirectResult` to `/app/` (302), then delete after redirect proven

**Backend — delete:**
- `ProjectCeres/Views/Dashboard/Index.cshtml`
- `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml`
- `ProjectCeres/Controllers/DashboardController.cs` (after redirect moved or replaced)

**Frontend — create:**
- `ProjectCeres.Client/src/app/lib/chart-colors.ts`
- `ProjectCeres.Client/src/app/lib/chart-colors.test.ts`
- `ProjectCeres.Client/src/app/lib/format-month.ts`
- `ProjectCeres.Client/src/app/lib/format-month.test.ts`
- `ProjectCeres.Client/src/app/features/dashboard/charts-api.ts` — DTO types + URL constants for the 5 endpoints
- `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx` (new SPA version)
- `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.test.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.test.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.test.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.test.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx`
- `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.test.tsx`

**Frontend — modify:**
- `ProjectCeres.Client/src/app/pages/Dashboard.tsx` — add charts section to layout
- `ProjectCeres.Client/src/main.tsx` — remove the 5 chart `data-react` mount blocks

**Frontend — delete:**
- `ProjectCeres.Client/src/components/NetWorthChart.tsx` and `.test.tsx`
- `ProjectCeres.Client/src/components/IncomeExpenseChart.tsx` and `.test.tsx`
- `ProjectCeres.Client/src/components/SpendingByCategoryChart.tsx` and `.test.tsx`
- `ProjectCeres.Client/src/components/AccountBalancesChart.tsx` and `.test.tsx`
- `ProjectCeres.Client/src/components/CashFlowChart.tsx` and `.test.tsx`

**Docs — modify:**
- `docs/api-contract.md` — update 5 chart endpoint rows
- `docs/planning-phase3-spa-migration.md` — mark Dashboard fully migrated; note the 302 redirect is live
- `docs/planning-phase3.md` — mark Dashboard SPA migration item complete
- `docs/planning-future.md` — add "Settings-aware formatting" follow-up

---

## Task Order Rationale

Backend DTOs and endpoints first (Tasks 1–5), so frontend can consume the real shapes. Then frontend utils (Tasks 6–7), then chart components one at a time (Tasks 8–12) — each fully tested and committed before moving on. Then layout integration (Task 13). Finally Razor cleanup (Task 14) and docs (Task 15).

---

### Task 1: Add backend chart DTOs

**Files:**
- Create: `ProjectCeres/ViewModels/DashboardChartDtos.cs`

- [ ] **Step 1: Create the DTOs file**

```csharp
namespace ProjectCeres.ViewModels;

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

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/ViewModels/DashboardChartDtos.cs
git commit -m "feat(dashboard): add typed DTOs for chart endpoints"
```

---

### Task 2: Refactor `net-worth-trend` endpoint

**Files:**
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs` — replace `GetNetWorthTrend()` (lines ~100–175)
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — replace `GetNetWorthTrend_Returns200_WithExpectedShape` (around line 56)

- [ ] **Step 1: Update the integration test for new shape and 12-month window**

In `DashboardApiTests.cs`, replace `GetNetWorthTrend_Returns200_WithExpectedShape` with:

```csharp
[Fact]
public async Task GetNetWorthTrend_Returns200_WithWrappedShape_And12MonthWindow()
{
    var response = await _client.GetAsync("/api/dashboard/net-worth-trend");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);

    body.TryGetProperty("currencyCode", out var code).Should().BeTrue();
    code.GetString().Should().NotBeNullOrEmpty();

    body.TryGetProperty("currencySymbol", out var symbol).Should().BeTrue();
    symbol.GetString().Should().NotBeNullOrEmpty();

    body.TryGetProperty("points", out var points).Should().BeTrue();
    points.ValueKind.Should().Be(JsonValueKind.Array);
    points.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(12);

    foreach (var item in points.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("assets", out _).Should().BeTrue();
        item.TryGetProperty("liabilities", out _).Should().BeTrue();
        item.TryGetProperty("netWorth", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetNetWorthTrend"`
Expected: FAIL — old endpoint returns array, not object.

- [ ] **Step 3: Refactor the endpoint to return `NetWorthTrendDto` with 12-month window**

In `DashboardApiController.cs`, replace `GetNetWorthTrend()` body with:

```csharp
[HttpGet("net-worth-trend")]
public async Task<IActionResult> GetNetWorthTrend()
{
    var settings = await settingsService.GetAsync();
    var currencyId = settings.DefaultCurrencyId;

    var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

    var today = DateOnly.FromDateTime(DateTime.Today);

    var accounts = await db.Accounts
        .Where(a => a.IsActive && a.CurrencyId == currencyId)
        .Include(a => a.AccountType)
        .Include(a => a.Transactions)
            .ThenInclude(t => t.Category)
                .ThenInclude(c => c.CategoryType)
        .AsNoTracking()
        .ToListAsync();

    var accountIds = accounts.Select(a => a.Id).ToHashSet();
    var allLiabilityPayments = await db.LiabilityPayments
        .Where(p => accountIds.Contains(p.AssetAccountId) || accountIds.Contains(p.LiabilityAccountId))
        .AsNoTracking()
        .ToListAsync();

    var points = new List<NetWorthTrendPoint>();

    for (int i = 11; i >= 0; i--)
    {
        var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(-i + 1).AddDays(-1);
        if (monthEnd > today) monthEnd = today;

        var monthLabel = new DateOnly(monthEnd.Year, monthEnd.Month, 1);

        var paymentsUpToMonth = allLiabilityPayments.Where(p => p.Date <= monthEnd).ToList();
        var paymentsByAsset = paymentsUpToMonth
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var paymentsByLiability = paymentsUpToMonth
            .GroupBy(p => p.LiabilityAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        decimal assets = 0;
        decimal liabilities = 0;

        foreach (var account in accounts)
        {
            bool isLiability = account.AccountType.Name == "Liability";

            var balance = account.Transactions
                .Where(t => t.Date <= monthEnd)
                .Sum(t =>
                {
                    if (t.Category.IsSystem) return t.Amount;
                    bool isIncome = t.Category.CategoryType.Name == "Income";
                    bool addsToBalance = isLiability ? !isIncome : isIncome;
                    return addsToBalance ? t.Amount : -t.Amount;
                });

            balance -= paymentsByAsset.GetValueOrDefault(account.Id);
            balance -= paymentsByLiability.GetValueOrDefault(account.Id);

            if (!isLiability) assets += balance;
            else liabilities += balance;
        }

        points.Add(new NetWorthTrendPoint(
            Month: monthLabel.ToString("yyyy-MM"),
            Assets: Math.Round(assets, 2),
            Liabilities: Math.Round(liabilities, 2),
            NetWorth: Math.Round(assets - liabilities, 2)));
    }

    return Ok(new NetWorthTrendDto(currency.Code, currency.Symbol, points));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetNetWorthTrend"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "refactor(dashboard): wrap net-worth-trend in typed DTO with 12-month window"
```

---

### Task 3: Refactor `income-expense` endpoint

**Files:**
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs` — replace `GetIncomeExpense()` (lines ~177–216)
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — replace `GetIncomeExpense_Returns200_WithExpectedShape`

- [ ] **Step 1: Update the integration test**

In `DashboardApiTests.cs`, replace `GetIncomeExpense_Returns200_WithExpectedShape` with:

```csharp
[Fact]
public async Task GetIncomeExpense_Returns200_WithWrappedShape_And12MonthWindow()
{
    var response = await _client.GetAsync("/api/dashboard/income-expense");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);
    body.TryGetProperty("currencyCode", out var code).Should().BeTrue();
    code.GetString().Should().NotBeNullOrEmpty();
    body.TryGetProperty("currencySymbol", out var symbol).Should().BeTrue();
    symbol.GetString().Should().NotBeNullOrEmpty();
    body.TryGetProperty("points", out var points).Should().BeTrue();
    points.ValueKind.Should().Be(JsonValueKind.Array);
    points.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(12);

    foreach (var item in points.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("income", out _).Should().BeTrue();
        item.TryGetProperty("expenses", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetIncomeExpense"`
Expected: FAIL — endpoint still returns array.

- [ ] **Step 3: Refactor the endpoint**

In `DashboardApiController.cs`, replace `GetIncomeExpense()` with:

```csharp
[HttpGet("income-expense")]
public async Task<IActionResult> GetIncomeExpense()
{
    var settings = await settingsService.GetAsync();
    var currencyId = settings.DefaultCurrencyId;
    var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

    var today = DateOnly.FromDateTime(DateTime.Today);
    var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);

    var allTransactions = await db.Transactions
        .Where(t => t.Date >= windowStart && t.Date <= today &&
                    t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
        .Include(t => t.Category)
            .ThenInclude(c => c.CategoryType)
        .AsNoTracking()
        .ToListAsync();

    var points = new List<IncomeExpensePoint>();

    for (int i = 11; i >= 0; i--)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
        var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
        if (monthEnd > today) monthEnd = today;

        var monthTx = allTransactions.Where(t => t.Date >= monthStart && t.Date <= monthEnd).ToList();
        var income   = monthTx.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var expenses = monthTx.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

        points.Add(new IncomeExpensePoint(
            Month: monthStart.ToString("yyyy-MM"),
            Income: Math.Round(income, 2),
            Expenses: Math.Round(expenses, 2)));
    }

    return Ok(new IncomeExpenseDto(currency.Code, currency.Symbol, points));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetIncomeExpense"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "refactor(dashboard): wrap income-expense in typed DTO with 12-month window"
```

---

### Task 4: Refactor `spending-by-category` endpoint (adds total)

**Files:**
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs` — replace `GetSpendingByCategory()` (lines ~218–248)
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — replace `GetSpendingByCategory_Returns200_WithExpectedShape`

- [ ] **Step 1: Update the integration test**

In `DashboardApiTests.cs`, replace `GetSpendingByCategory_Returns200_WithExpectedShape` with:

```csharp
[Fact]
public async Task GetSpendingByCategory_Returns200_WithWrappedShape_AndTotal()
{
    var response = await _client.GetAsync("/api/dashboard/spending-by-category");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);
    body.TryGetProperty("currencyCode", out var code).Should().BeTrue();
    code.GetString().Should().NotBeNullOrEmpty();
    body.TryGetProperty("currencySymbol", out _).Should().BeTrue();
    body.TryGetProperty("total", out var total).Should().BeTrue();
    total.ValueKind.Should().Be(JsonValueKind.Number);
    body.TryGetProperty("slices", out var slices).Should().BeTrue();
    slices.ValueKind.Should().Be(JsonValueKind.Array);

    foreach (var item in slices.EnumerateArray())
    {
        item.TryGetProperty("categoryName", out _).Should().BeTrue();
        item.TryGetProperty("amount", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetSpendingByCategory"`
Expected: FAIL.

- [ ] **Step 3: Refactor the endpoint**

In `DashboardApiController.cs`, replace `GetSpendingByCategory()` with:

```csharp
[HttpGet("spending-by-category")]
public async Task<IActionResult> GetSpendingByCategory()
{
    var settings = await settingsService.GetAsync();
    var currencyId = settings.DefaultCurrencyId;
    var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

    var today = DateOnly.FromDateTime(DateTime.Today);
    var monthStart = new DateOnly(today.Year, today.Month, 1);

    var transactions = await db.Transactions
        .Where(t => t.Date >= monthStart && t.Date <= today &&
                    t.Account.CurrencyId == currencyId &&
                    t.Category.CategoryType.Name == "Expense" &&
                    !t.Category.IsSystem)
        .Include(t => t.Category)
            .ThenInclude(c => c.CategoryType)
        .AsNoTracking()
        .ToListAsync();

    var slices = transactions
        .GroupBy(t => t.Category.Name)
        .Select(g => new SpendingByCategorySlice(
            CategoryName: g.Key,
            Amount: Math.Round(g.Sum(t => t.Amount), 2)))
        .OrderByDescending(x => x.Amount)
        .ToList();

    var total = Math.Round(slices.Sum(s => s.Amount), 2);

    return Ok(new SpendingByCategoryDto(currency.Code, currency.Symbol, total, slices));
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetSpendingByCategory"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "refactor(dashboard): wrap spending-by-category in typed DTO with total"
```

---

### Task 5: Refactor `account-balances` and `cash-flow` endpoints

**Files:**
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs` — replace both `GetAccountBalances()` and `GetCashFlow()`
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` — replace both tests

- [ ] **Step 1: Update integration tests**

In `DashboardApiTests.cs`, replace `GetAccountBalances_Returns200_WithExpectedShape` and `GetCashFlow_Returns200_WithExpectedShape` with:

```csharp
[Fact]
public async Task GetAccountBalances_Returns200_WithWrappedShape()
{
    var response = await _client.GetAsync("/api/dashboard/account-balances");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);
    body.TryGetProperty("currencyCode", out var code).Should().BeTrue();
    code.GetString().Should().NotBeNullOrEmpty();
    body.TryGetProperty("currencySymbol", out _).Should().BeTrue();
    body.TryGetProperty("rows", out var rows).Should().BeTrue();
    rows.ValueKind.Should().Be(JsonValueKind.Array);

    foreach (var item in rows.EnumerateArray())
    {
        item.TryGetProperty("accountName", out _).Should().BeTrue();
        item.TryGetProperty("balance", out _).Should().BeTrue();
    }
}

[Fact]
public async Task GetCashFlow_Returns200_WithWrappedShape_And12MonthWindow()
{
    var response = await _client.GetAsync("/api/dashboard/cash-flow");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Object);
    body.TryGetProperty("currencyCode", out var code).Should().BeTrue();
    code.GetString().Should().NotBeNullOrEmpty();
    body.TryGetProperty("currencySymbol", out _).Should().BeTrue();
    body.TryGetProperty("points", out var points).Should().BeTrue();
    points.ValueKind.Should().Be(JsonValueKind.Array);
    points.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(12);

    foreach (var item in points.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("netFlow", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetAccountBalances|FullyQualifiedName~GetCashFlow"`
Expected: FAIL on both.

- [ ] **Step 3: Refactor `GetAccountBalances()`**

In `DashboardApiController.cs`, replace `GetAccountBalances()` with:

```csharp
[HttpGet("account-balances")]
public async Task<IActionResult> GetAccountBalances()
{
    var settings = await settingsService.GetAsync();
    var currencyId = settings.DefaultCurrencyId;
    var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

    var accounts = await db.Accounts
        .Where(a => a.IsActive && a.CurrencyId == currencyId && a.AccountType.Name != "Liability")
        .AsNoTracking()
        .ToListAsync();

    var rowList = new List<AccountBalanceRow>();
    foreach (var account in accounts)
    {
        var balance = await accountService.GetBalanceAsync(account.Id);
        rowList.Add(new AccountBalanceRow(account.Name, Math.Round(balance, 2)));
    }

    var rows = rowList.OrderByDescending(r => r.Balance).ToList();

    return Ok(new AccountBalancesDto(currency.Code, currency.Symbol, rows));
}
```

- [ ] **Step 4: Refactor `GetCashFlow()` with 12-month window**

In `DashboardApiController.cs`, replace `GetCashFlow()` with:

```csharp
[HttpGet("cash-flow")]
public async Task<IActionResult> GetCashFlow()
{
    var settings = await settingsService.GetAsync();
    var currencyId = settings.DefaultCurrencyId;
    var currency = await db.Currencies.AsNoTracking().FirstAsync(c => c.Id == currencyId);

    var today = DateOnly.FromDateTime(DateTime.Today);
    var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);

    var allTransactions = await db.Transactions
        .Where(t => t.Date >= windowStart && t.Date <= today &&
                    t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
        .Include(t => t.Category)
            .ThenInclude(c => c.CategoryType)
        .AsNoTracking()
        .ToListAsync();

    var points = new List<CashFlowPoint>();

    for (int i = 11; i >= 0; i--)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
        var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
        if (monthEnd > today) monthEnd = today;

        var monthTx  = allTransactions.Where(t => t.Date >= monthStart && t.Date <= monthEnd).ToList();
        var income   = monthTx.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
        var expenses = monthTx.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

        points.Add(new CashFlowPoint(
            Month: monthStart.ToString("yyyy-MM"),
            NetFlow: Math.Round(income - expenses, 2)));
    }

    return Ok(new CashFlowDto(currency.Code, currency.Symbol, points));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetAccountBalances|FullyQualifiedName~GetCashFlow"`
Expected: PASS on both.

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test ProjectCeres.Tests`
Expected: All pass.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "refactor(dashboard): wrap account-balances and cash-flow in typed DTOs"
```

---

### Task 6: Add `chart-colors` util

**Files:**
- Create: `ProjectCeres.Client/src/app/lib/chart-colors.ts`
- Create: `ProjectCeres.Client/src/app/lib/chart-colors.test.ts`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/lib/chart-colors.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { chartColors } from './chart-colors';

describe('chartColors', () => {
  it('exposes semantic tokens as CSS var strings', () => {
    expect(chartColors.income).toBe('var(--success)');
    expect(chartColors.expense).toBe('var(--destructive)');
    expect(chartColors.netWorth).toBe('var(--chart-1)');
    expect(chartColors.assets).toBe('var(--chart-2)');
    expect(chartColors.liabilities).toBe('var(--chart-3)');
  });

  it('returns the requested chart palette slot', () => {
    expect(chartColors.slot(1)).toBe('var(--chart-1)');
    expect(chartColors.slot(8)).toBe('var(--chart-8)');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/chart-colors.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `chart-colors.ts`**

Create `ProjectCeres.Client/src/app/lib/chart-colors.ts`:

```ts
export const chartColors = {
  income: 'var(--success)',
  expense: 'var(--destructive)',
  netWorth: 'var(--chart-1)',
  assets: 'var(--chart-2)',
  liabilities: 'var(--chart-3)',
  slot: (n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8): string => `var(--chart-${n})`,
};
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/chart-colors.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/lib/chart-colors.ts ProjectCeres.Client/src/app/lib/chart-colors.test.ts
git commit -m "feat(dashboard): add chartColors util for Recharts CSS-var wiring"
```

---

### Task 7: Add `format-month` util

**Files:**
- Create: `ProjectCeres.Client/src/app/lib/format-month.ts`
- Create: `ProjectCeres.Client/src/app/lib/format-month.test.ts`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/lib/format-month.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { formatMonth } from './format-month';

describe('formatMonth', () => {
  it('formats yyyy-MM as "MMM yyyy"', () => {
    // jsdom defaults to en-US locale
    expect(formatMonth('2026-04')).toBe('Apr 2026');
    expect(formatMonth('2025-12')).toBe('Dec 2025');
    expect(formatMonth('2026-01')).toBe('Jan 2026');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/format-month.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `format-month.ts`**

Create `ProjectCeres.Client/src/app/lib/format-month.ts`:

```ts
/** Formats "yyyy-MM" → "MMM yyyy" using the runtime locale. */
export function formatMonth(yyyyMm: string): string {
  const [y, m] = yyyyMm.split('-').map(Number);
  return new Date(y, m - 1).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/lib/format-month.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/lib/format-month.ts ProjectCeres.Client/src/app/lib/format-month.test.ts
git commit -m "feat(dashboard): add formatMonth helper for chart axis labels"
```

---

### Task 8: Add `charts-api.ts` (frontend DTO types + URL constants)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/dashboard/charts-api.ts`

- [ ] **Step 1: Create the file**

Create `ProjectCeres.Client/src/app/features/dashboard/charts-api.ts`:

```ts
export const NET_WORTH_TREND_URL          = '/api/dashboard/net-worth-trend';
export const INCOME_EXPENSE_URL           = '/api/dashboard/income-expense';
export const SPENDING_BY_CATEGORY_URL     = '/api/dashboard/spending-by-category';
export const ACCOUNT_BALANCES_URL         = '/api/dashboard/account-balances';
export const CASH_FLOW_URL                = '/api/dashboard/cash-flow';

export type NetWorthTrendPoint = {
  month: string;
  assets: number;
  liabilities: number;
  netWorth: number;
};
export type NetWorthTrendDto = {
  currencyCode: string;
  currencySymbol: string;
  points: NetWorthTrendPoint[];
};

export type IncomeExpensePoint = {
  month: string;
  income: number;
  expenses: number;
};
export type IncomeExpenseDto = {
  currencyCode: string;
  currencySymbol: string;
  points: IncomeExpensePoint[];
};

export type SpendingByCategorySlice = {
  categoryName: string;
  amount: number;
};
export type SpendingByCategoryDto = {
  currencyCode: string;
  currencySymbol: string;
  total: number;
  slices: SpendingByCategorySlice[];
};

export type AccountBalanceRow = {
  accountName: string;
  balance: number;
};
export type AccountBalancesDto = {
  currencyCode: string;
  currencySymbol: string;
  rows: AccountBalanceRow[];
};

export type CashFlowPoint = {
  month: string;
  netFlow: number;
};
export type CashFlowDto = {
  currencyCode: string;
  currencySymbol: string;
  points: CashFlowPoint[];
};
```

- [ ] **Step 2: Verify it type-checks**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/charts-api.ts
git commit -m "feat(dashboard): add typed DTOs and URL constants for chart endpoints"
```

---

### Task 9: Build `NetWorthChart` (SPA version)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NetWorthChart } from './NetWorthChart';

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('NetWorthChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {})); // never resolves
    render(<NetWorthChart />);
    expect(screen.getByText('Net Worth Over Time')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<NetWorthChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<NetWorthChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Net Worth Over Time/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [
          { month: '2026-03', assets: 1000, liabilities: 200, netWorth: 800 },
          { month: '2026-04', assets: 1100, liabilities: 200, netWorth: 900 },
        ],
      }),
    });
    const { container } = render(<NetWorthChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/NetWorthChart.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `NetWorthChart.tsx`**

Create `ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx`:

```tsx
import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Legend } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from './CardError';
import { NET_WORTH_TREND_URL, type NetWorthTrendDto } from './charts-api';

export function NetWorthChart() {
  const { data, error, loading, refetch } = useApi<NetWorthTrendDto>(NET_WORTH_TREND_URL);

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
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={data.points}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} tickFormatter={formatMonth} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip
                labelFormatter={formatMonth}
                formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`}
              />
              <Legend />
              <Line type="monotone" dataKey="assets" stroke={chartColors.assets} name="Assets" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="netWorth" stroke={chartColors.netWorth} name="Net Worth" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/NetWorthChart.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.tsx ProjectCeres.Client/src/app/features/dashboard/NetWorthChart.test.tsx
git commit -m "feat(dashboard): add SPA NetWorthChart using useApi + chartColors"
```

---

### Task 10: Build `IncomeExpenseChart` (SPA version)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { IncomeExpenseChart } from './IncomeExpenseChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('IncomeExpenseChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<IncomeExpenseChart />);
    expect(screen.getByText('Income vs Expense')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Income vs Expense/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [{ month: '2026-04', income: 3000, expenses: 2000 }],
      }),
    });
    const { container } = render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/IncomeExpenseChart.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `IncomeExpenseChart.tsx`**

Create `ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx`:

```tsx
import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Legend } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from './CardError';
import { INCOME_EXPENSE_URL, type IncomeExpenseDto } from './charts-api';

export function IncomeExpenseChart() {
  const { data, error, loading, refetch } = useApi<IncomeExpenseDto>(INCOME_EXPENSE_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Income vs Expense</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Income vs Expense" onRetry={refetch} />}
        {data && data.points.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.points.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data.points}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} tickFormatter={formatMonth} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip
                labelFormatter={formatMonth}
                formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`}
              />
              <Legend />
              <Bar dataKey="income" fill={chartColors.income} name="Income" />
              <Bar dataKey="expenses" fill={chartColors.expense} name="Expenses" />
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/IncomeExpenseChart.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.tsx ProjectCeres.Client/src/app/features/dashboard/IncomeExpenseChart.test.tsx
git commit -m "feat(dashboard): add SPA IncomeExpenseChart using semantic colors"
```

---

### Task 11: Build `SpendingByCategoryChart` (SPA version)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.test.tsx`

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SpendingByCategoryChart } from './SpendingByCategoryChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('SpendingByCategoryChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<SpendingByCategoryChart />);
    expect(screen.getByText('Spending by Category')).toBeInTheDocument();
    expect(screen.getByText('This month')).toBeInTheDocument();
  });

  it('renders empty state when slices is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', total: 0, slices: [] }),
    });
    render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Spending by Category/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        total: 500,
        slices: [
          { categoryName: 'Groceries', amount: 300 },
          { categoryName: 'Transport', amount: 200 },
        ],
      }),
    });
    const { container } = render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/SpendingByCategoryChart.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `SpendingByCategoryChart.tsx`**

Create `ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx`:

```tsx
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { CardError } from './CardError';
import { SPENDING_BY_CATEGORY_URL, type SpendingByCategoryDto } from './charts-api';

const SLOT_COUNT = 8;
type Slot = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export function SpendingByCategoryChart() {
  const { data, error, loading, refetch } = useApi<SpendingByCategoryDto>(SPENDING_BY_CATEGORY_URL);

  const top = data?.slices.slice(0, 10) ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Spending by Category</CardTitle>
        <p className="text-xs text-muted-foreground">This month</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Spending by Category" onRetry={refetch} />}
        {data && data.slices.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.slices.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={top} layout="vertical" margin={{ left: 12, right: 12 }}>
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="categoryName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip
                formatter={(v) => {
                  const amount = Number(v ?? 0);
                  const pct = data.total > 0 ? ((amount / data.total) * 100).toFixed(1) : '0.0';
                  return `${data.currencySymbol} ${amount.toFixed(2)} (${pct}%)`;
                }}
              />
              <Bar dataKey="amount" name="Amount">
                {top.map((_, i) => (
                  <Cell key={i} fill={chartColors.slot((((i % SLOT_COUNT) + 1) as Slot))} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/SpendingByCategoryChart.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.tsx ProjectCeres.Client/src/app/features/dashboard/SpendingByCategoryChart.test.tsx
git commit -m "feat(dashboard): add SPA SpendingByCategoryChart with % of total tooltip"
```

---

### Task 12: Build `AccountBalancesChart` and `CashFlowChart`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx`
- Create: `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.test.tsx`

- [ ] **Step 1: Write the AccountBalancesChart test**

Create `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountBalancesChart } from './AccountBalancesChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('AccountBalancesChart', () => {
  it('renders title without a subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<AccountBalancesChart />);
    expect(screen.getByText('Account Balances')).toBeInTheDocument();
    expect(screen.queryByText(/Last \d+ months/)).not.toBeInTheDocument();
  });

  it('renders empty state when rows is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', rows: [] }),
    });
    render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Account Balances/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        rows: [{ accountName: 'Checking', balance: 5000 }],
      }),
    });
    const { container } = render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 2: Implement AccountBalancesChart.tsx**

Create `ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx`:

```tsx
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { CardError } from './CardError';
import { ACCOUNT_BALANCES_URL, type AccountBalancesDto } from './charts-api';

const SLOT_COUNT = 8;
type Slot = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export function AccountBalancesChart() {
  const { data, error, loading, refetch } = useApi<AccountBalancesDto>(ACCOUNT_BALANCES_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Account Balances</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Account Balances" onRetry={refetch} />}
        {data && data.rows.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.rows.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data.rows} layout="vertical" margin={{ left: 12, right: 12 }}>
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="accountName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`} />
              <Bar dataKey="balance" name="Balance">
                {data.rows.map((_, i) => (
                  <Cell key={i} fill={chartColors.slot((((i % SLOT_COUNT) + 1) as Slot))} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3: Run AccountBalancesChart test**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/AccountBalancesChart.test.tsx`
Expected: PASS.

- [ ] **Step 4: Write the CashFlowChart test**

Create `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CashFlowChart } from './CashFlowChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('CashFlowChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<CashFlowChart />);
    expect(screen.getByText('Cash Flow')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<CashFlowChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<CashFlowChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Cash Flow/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [
          { month: '2026-03', netFlow: 500 },
          { month: '2026-04', netFlow: -200 },
        ],
      }),
    });
    const { container } = render(<CashFlowChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
```

- [ ] **Step 5: Implement CashFlowChart.tsx**

Create `ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx`:

```tsx
import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from './CardError';
import { CASH_FLOW_URL, type CashFlowDto } from './charts-api';

export function CashFlowChart() {
  const { data, error, loading, refetch } = useApi<CashFlowDto>(CASH_FLOW_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Cash Flow</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Cash Flow" onRetry={refetch} />}
        {data && data.points.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.points.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data.points}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} tickFormatter={formatMonth} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip
                labelFormatter={formatMonth}
                formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`}
              />
              <Bar dataKey="netFlow" name="Net Flow">
                {data.points.map((p, i) => (
                  <Cell key={i} fill={p.netFlow >= 0 ? chartColors.income : chartColors.expense} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 6: Run CashFlowChart test**

Run: `cd ProjectCeres.Client && pnpm vitest run src/app/features/dashboard/CashFlowChart.test.tsx`
Expected: PASS.

- [ ] **Step 7: Run the full client test suite**

Run: `cd ProjectCeres.Client && pnpm test`
Expected: All pass.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.tsx ProjectCeres.Client/src/app/features/dashboard/AccountBalancesChart.test.tsx ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.tsx ProjectCeres.Client/src/app/features/dashboard/CashFlowChart.test.tsx
git commit -m "feat(dashboard): add SPA AccountBalancesChart and CashFlowChart"
```

---

### Task 13: Wire charts into the SPA Dashboard layout

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Dashboard.tsx`

- [ ] **Step 1: Update `Dashboard.tsx`**

Replace the contents of `ProjectCeres.Client/src/app/pages/Dashboard.tsx` with:

```tsx
import { useEffect, useRef } from 'react';
import { AccountBalancesChart } from '../features/dashboard/AccountBalancesChart';
import { CashFlowChart } from '../features/dashboard/CashFlowChart';
import { CategoryBudgetsCard } from '../features/dashboard/CategoryBudgetsCard';
import { FinancialHealthCard } from '../features/dashboard/FinancialHealthCard';
import { GoalBudgetsCard } from '../features/dashboard/GoalBudgetsCard';
import { IncomeExpenseChart } from '../features/dashboard/IncomeExpenseChart';
import { KpiStrip } from '../features/dashboard/KpiStrip';
import { NetWorthChart } from '../features/dashboard/NetWorthChart';
import { SpendingByCategoryChart } from '../features/dashboard/SpendingByCategoryChart';

export function Dashboard() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="space-y-6">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-3xl font-semibold outline-none"
      >
        Dashboard
      </h1>

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
  );
}
```

- [ ] **Step 2: Type-check and run all client tests**

Run: `cd ProjectCeres.Client && pnpm tsc --noEmit && pnpm test`
Expected: No TS errors. All tests pass.

- [ ] **Step 3: Manual smoke test**

Run: `dotnet run --project ProjectCeres --launch-profile https`
In a browser, open `https://localhost:7081/app/`. Confirm: dashboard renders all 5 chart cards (NetWorth full-width, then 2x2). Each card either shows the chart, the empty state, or a skeleton — never blank.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/Dashboard.tsx
git commit -m "feat(dashboard): wire 5 charts into SPA dashboard layout"
```

---

### Task 14: Razor cleanup (302 redirect + delete views/controller + remove mount blocks)

**Files:**
- Modify: `ProjectCeres/Controllers/DashboardController.cs` — convert `Index()` to 302 redirect
- Modify: `ProjectCeres.Client/src/main.tsx` — remove the 5 chart `data-react` mount blocks
- Delete: `ProjectCeres/Views/Dashboard/Index.cshtml`
- Delete: `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml`
- Delete: `ProjectCeres.Client/src/components/NetWorthChart.tsx`, `IncomeExpenseChart.tsx`, `SpendingByCategoryChart.tsx`, `AccountBalancesChart.tsx`, `CashFlowChart.tsx` and their `.test.tsx` siblings
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs` (or new file) — add 302 redirect assertion

- [ ] **Step 1: Write a redirect integration test**

Append to `ProjectCeres.Tests/Integration/DashboardApiTests.cs` (the file already exists and uses `factory.CreateClient()`):

```csharp
[Fact]
public async Task GetDashboardRoot_Returns302_RedirectingToAppShell()
{
    using var noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    var response = await noRedirectClient.GetAsync("/Dashboard");

    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location.Should().NotBeNull();
    response.Headers.Location!.ToString().Should().Be("/app/");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetDashboardRoot_Returns302"`
Expected: FAIL — current `DashboardController.Index()` returns the Razor view (200 OK).

- [ ] **Step 3: Convert `DashboardController` to a redirect**

Replace the contents of `ProjectCeres/Controllers/DashboardController.cs` with:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class DashboardController : Controller
{
    public IActionResult Index() => Redirect("/app/");
}
```

- [ ] **Step 4: Run the redirect test**

Run: `dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~GetDashboardRoot_Returns302"`
Expected: PASS.

- [ ] **Step 5: Delete the Razor view files**

Run:

```bash
rm ProjectCeres/Views/Dashboard/Index.cshtml
rm ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml
rmdir ProjectCeres/Views/Dashboard
```

- [ ] **Step 6: Remove the 5 chart mount blocks from `main.tsx`**

Open `ProjectCeres.Client/src/main.tsx` and delete the blocks for these selectors (each is roughly 8 lines with `querySelector` + `if` + `createRoot().render()`):
- `[data-react="net-worth-chart"]`
- `[data-react="income-expense-chart"]`
- `[data-react="spending-by-category-chart"]`
- `[data-react="account-balances-chart"]`
- `[data-react="cash-flow-chart"]`

Leave the other mount blocks (is-cleared-switch, cleared-badge, category-budget-bars, goal-budget-bars, confirm-dialog) alone.

Also delete the corresponding `import { ... } from './components/...Chart'` lines if any.

- [ ] **Step 7: Delete the old chart components and tests**

Run:

```bash
rm ProjectCeres.Client/src/components/NetWorthChart.tsx ProjectCeres.Client/src/components/NetWorthChart.test.tsx
rm ProjectCeres.Client/src/components/IncomeExpenseChart.tsx ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx
rm ProjectCeres.Client/src/components/SpendingByCategoryChart.tsx ProjectCeres.Client/src/components/SpendingByCategoryChart.test.tsx
rm ProjectCeres.Client/src/components/AccountBalancesChart.tsx ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx
rm ProjectCeres.Client/src/components/CashFlowChart.tsx ProjectCeres.Client/src/components/CashFlowChart.test.tsx
```

- [ ] **Step 8: Run all backend and frontend tests**

Run: `dotnet test ProjectCeres.Tests && cd ProjectCeres.Client && pnpm test`
Expected: All pass.

- [ ] **Step 9: Manual smoke test**

Run: `dotnet run --project ProjectCeres --launch-profile https`
In a browser:
- Open `https://localhost:7081/Dashboard` → should redirect (302) to `/app/` and render the SPA dashboard.
- Open `https://localhost:7081/app/` → confirm dashboard still renders cleanly with all charts.

- [ ] **Step 10: Commit**

```bash
git add ProjectCeres/Controllers/DashboardController.cs ProjectCeres/Views/Dashboard ProjectCeres.Client/src/main.tsx ProjectCeres.Client/src/components ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "chore(dashboard): retire Razor dashboard, redirect to SPA, drop legacy chart islands"
```

---

### Task 15: Update documentation

**Files:**
- Modify: `docs/api-contract.md` — update 5 chart endpoint rows
- Modify: `docs/planning-phase3-spa-migration.md` — mark Dashboard fully migrated; note 302 is live
- Modify: `docs/planning-phase3.md` — mark Dashboard SPA migration item complete (remove from Open Questions; add to Resolved if applicable, per the project rule)
- Modify: `docs/planning-future.md` — add Settings-aware formatting follow-up

- [ ] **Step 1: Update `docs/api-contract.md`**

Find the 5 chart endpoint rows (`/api/dashboard/net-worth-trend`, `income-expense`, `spending-by-category`, `account-balances`, `cash-flow`) and update each to reflect the new wrapper shape. Each row should now describe the wrapper DTO. Example for `net-worth-trend`:

```
GET /api/dashboard/net-worth-trend
→ NetWorthTrendDto { currencyCode, currencySymbol, points: NetWorthTrendPoint[12] }
  NetWorthTrendPoint { month: "yyyy-MM", assets, liabilities, netWorth }
```

Mirror the pattern for the other four (use the field names defined in `ProjectCeres/ViewModels/DashboardChartDtos.cs`).

- [ ] **Step 2: Update `docs/planning-phase3-spa-migration.md`**

In the per-area table (or equivalent section), mark Dashboard as fully migrated. Add a one-line note: "302 redirect from `/Dashboard` → `/app/` is live; Razor dashboard view, partial, and controller deleted." Leave the global `/app/*` → `/*` 301 plan unchanged (that happens later).

- [ ] **Step 3: Update `docs/planning-phase3.md`**

Find any open item or checkbox related to Dashboard SPA migration and mark it `[x]`. Per `CLAUDE.md`, if it was in Open Questions, move the resolved entry to `docs/planning-resolved.md`. Otherwise just check it off.

- [ ] **Step 4: Add the Settings-aware formatting entry to `docs/planning-future.md`**

Append (or merge into the relevant section):

```markdown
### Settings-aware formatting

- **What:** A `useSettings()` hook + `formatDate` / `formatMonth` / `formatNumber` / `formatPercent` utils that respect the user's `DateFormat` and `NumberFormat` preferences from `Settings`.
- **Why:** SPA currently formats dates/numbers with locale-default `Intl` calls. The Razor side already uses `NumberFormatHelper` for amounts and respects `Settings.DateFormat` / `Settings.NumberFormat`. The SPA needs parity before settings UI ships.
- **Scope:** Cross-cutting — touches charts (axis labels, tooltips), KPI cards, transaction lists, reports. Should land as one coordinated change rather than per-feature drift.
- **Triggered by:** Dashboard Phase 2 spec (`docs/superpowers/specs/2026-04-29-dashboard-phase-2-design.md`, §9 Out of Scope).
```

- [ ] **Step 5: Commit**

```bash
git add docs/api-contract.md docs/planning-phase3-spa-migration.md docs/planning-phase3.md docs/planning-future.md docs/planning-resolved.md
git commit -m "docs(dashboard): record Dashboard Phase 2 outcomes and follow-ups"
```

---

## Self-Review Notes

- **Spec coverage:**
  - §1 Goal — covered by full plan.
  - §2 Architecture — Tasks 1–5 (backend), Tasks 6–13 (frontend), Task 14 (Razor cleanup).
  - §3a DTOs — Task 1.
  - §3b chartColors — Task 6.
  - §3c formatMonth — Task 7.
  - §3d charts — Tasks 9–12 (one task per chart, plus charts-api in Task 8).
  - §4 Layout — Task 13.
  - §5 Razor cleanup — Task 14.
  - §6 Data flow / error handling — embedded in each chart task (Tasks 9–12).
  - §7 Testing strategy — every endpoint and component task includes its own test step; Task 14 adds the redirect integration test.
  - §8 Documentation updates — Task 15.
  - §9 Out-of-scope — recorded in `planning-future.md` (Task 15, Step 4).
- **Type consistency:** Field names (`points`, `slices`, `rows`, `currencyCode`, `currencySymbol`, `total`, `netFlow`, `categoryName`, `accountName`, `assets`, `liabilities`, `netWorth`, `income`, `expenses`, `amount`, `balance`, `month`) are identical between backend records (Task 1), frontend types (Task 8), and consumer components (Tasks 9–12). `Slot` type aliases use the same `1 | 2 | 3 | 4 | 5 | 6 | 7 | 8` union as `chartColors.slot` (Task 6).
- **Path consistency:** All chart components reference `'../../lib/use-api'`, `'../../lib/chart-colors'`, `'../../lib/format-month'`, and `'./CardError'` — matches the directory layout used by existing dashboard cards (e.g., `MtdCard.tsx` imports `'../../lib/use-api'` and `'./CardError'`).
- **Window length:** All trend endpoints (Tasks 2, 3, 5) use a 12-month window via `for (int i = 11; i >= 0; i--)`. Frontend subtitles say "Last 12 months" (Tasks 9, 10, 12).
