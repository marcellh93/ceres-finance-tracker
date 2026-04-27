# Stage 8.2 — Dashboard Chart Components Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 5 Recharts chart components to the existing dashboard page, each backed by a new API endpoint on `DashboardApiController`, following strict TDD (API test → endpoint → Vitest test → component → mount).

**Architecture:** Each chart is an independent React component that fetches its own data from a dedicated `GET /api/dashboard/*` endpoint. Components are mounted via the existing `data-react` / `querySelector` + `createRoot` pattern in `main.tsx`. The dashboard Razor view gets a new 2-col + 3-col chart grid appended below the budget bars section.

**Tech Stack:** ASP.NET Core 10, EF Core + PostgreSQL, Recharts, shadcn/ui `<ChartContainer>`, React 19, Vitest + React Testing Library, xUnit + FluentAssertions + WebApplicationFactory.

---

## File Map

| File | Status | Role |
|---|---|---|
| `ProjectCeres/Controllers/Api/DashboardApiController.cs` | Modify | Add 5 new GET endpoints |
| `ProjectCeres.Tests/Integration/DashboardApiTests.cs` | Modify | Add 5 integration tests |
| `ProjectCeres/Views/Dashboard/Index.cshtml` | Modify | Append chart mount points |
| `ProjectCeres.Client/src/main.tsx` | Modify | Mount 5 new components |
| `ProjectCeres.Client/src/components/NetWorthChart.tsx` | Create | Line chart |
| `ProjectCeres.Client/src/components/NetWorthChart.test.tsx` | Create | Vitest smoke test |
| `ProjectCeres.Client/src/components/IncomeExpenseChart.tsx` | Create | Grouped bar chart |
| `ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx` | Create | Vitest smoke test |
| `ProjectCeres.Client/src/components/SpendingDonutChart.tsx` | Create | Doughnut chart |
| `ProjectCeres.Client/src/components/SpendingDonutChart.test.tsx` | Create | Vitest smoke test |
| `ProjectCeres.Client/src/components/AccountBalancesChart.tsx` | Create | Horizontal bar chart |
| `ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx` | Create | Vitest smoke test |
| `ProjectCeres.Client/src/components/CashFlowChart.tsx` | Create | Bar chart with negative support |
| `ProjectCeres.Client/src/components/CashFlowChart.test.tsx` | Create | Vitest smoke test |

---

## Task 0: Install dependencies

**Files:** `ProjectCeres.Client/package.json`, `ProjectCeres.Client/src/components/ui/chart.tsx` (created by shadcn)

- [ ] **Step 1: Install Recharts and the shadcn/ui chart component**

```bash
cd ProjectCeres.Client
pnpm add recharts
pnpm dlx shadcn@latest add chart
```

Expected: `recharts` appears in `package.json` dependencies. `src/components/ui/chart.tsx` is created by shadcn.

- [ ] **Step 2: Verify the chart component exists**

```bash
ls ProjectCeres.Client/src/components/ui/chart.tsx
```

Expected: file exists.

- [ ] **Step 3: Run existing Vitest tests to confirm nothing broke**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: all existing tests pass (0 failed).

- [ ] **Step 4: Commit**

```bash
cd ProjectCeres.Client && git add package.json pnpm-lock.yaml src/components/ui/chart.tsx
cd .. && git commit -m "chore: install recharts and shadcn chart component"
```

---

## Task 1: Net Worth Trend — API endpoint + test

**Context:** Monthly net worth = assets − liabilities for the default currency, computed for each of the last 6 months. Account balance is always derived (never stored). Balance = SUM of transactions where income adds to balance (expense subtracts), adjusted for liability payments. Match the logic in `ReportService.GetNetWorthAsync` but scoped to 6 monthly snapshots.

**Files:**
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs`
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs`

- [ ] **Step 1: Write the failing integration test**

Open `ProjectCeres.Tests/Integration/DashboardApiTests.cs` and append this test to the `DashboardApiTests` class:

```csharp
[Fact]
public async Task GetNetWorthTrend_Returns200_WithExpectedShape()
{
    var response = await _client.GetAsync("/api/dashboard/net-worth-trend");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Array);

    // Should return up to 6 monthly entries
    body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessOrEqualTo(6);

    foreach (var item in body.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("assets", out _).Should().BeTrue();
        item.TryGetProperty("liabilities", out _).Should().BeTrue();
        item.TryGetProperty("netWorth", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd <repo>
dotnet test --filter "GetNetWorthTrend_Returns200_WithExpectedShape"
```

Expected: FAIL with 404 (endpoint doesn't exist yet).

- [ ] **Step 3: Add the endpoint to DashboardApiController**

Open `ProjectCeres/Controllers/Api/DashboardApiController.cs`. Add the `ISettingsService` constructor parameter and the new endpoint. The full file should look like:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/dashboard")]
public class DashboardApiController(
    ICategoryBudgetService categoryBudgetService,
    IBudgetService budgetService,
    ISettingsService settingsService,
    AppDbContext db) : ControllerBase
{
    [HttpGet("category-budgets")]
    public async Task<IActionResult> GetCategoryBudgets()
    {
        var budgets = await categoryBudgetService.GetAllAsync(includeInactive: false);
        var now = DateTime.Today;

        var result = new List<object>();
        foreach (var budget in budgets)
        {
            var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, now.Year, now.Month);
            var percentUsed = budget.LimitAmount == 0m
                ? 0m
                : Math.Round(spent / budget.LimitAmount * 100m, 2);

            result.Add(new
            {
                id             = budget.Id,
                categoryName   = budget.Category.Name,
                currencyCode   = budget.Currency.Code,
                currencySymbol = budget.Currency.Symbol,
                spent          = spent,
                limit          = budget.LimitAmount,
                percentUsed    = percentUsed
            });
        }

        return Ok(result);
    }

    [HttpGet("goal-budgets")]
    public async Task<IActionResult> GetGoalBudgets()
    {
        var goals = await budgetService.GetAllAsync(includeInactive: false);

        var result = new List<object>();
        foreach (var goal in goals)
        {
            var progress = await budgetService.GetProgressAsync(goal.Id);

            result.Add(new
            {
                id             = goal.Id,
                name           = goal.Name,
                goalType       = goal.GoalType,
                amountProgress = progress.AmountProgress,
                targetAmount   = progress.TargetAmount,
                remaining      = progress.Remaining,
                percentUsed    = progress.PercentUsed,
                currencyCode   = goal.Currency.Code,
                currencySymbol = goal.Currency.Symbol
            });
        }

        return Ok(result);
    }

    [HttpGet("net-worth-trend")]
    public async Task<IActionResult> GetNetWorthTrend()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = new List<object>();

        for (int i = 5; i >= 0; i--)
        {
            var monthEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(-i + 1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var monthLabel = new DateOnly(monthEnd.Year, monthEnd.Month, 1);

            var accounts = await db.Accounts
                .Where(a => a.IsActive && a.CurrencyId == currencyId)
                .Include(a => a.AccountType)
                .Include(a => a.Transactions.Where(t => t.Date <= monthEnd))
                    .ThenInclude(t => t.Category)
                        .ThenInclude(c => c.CategoryType)
                .ToListAsync();

            var accountIds = accounts.Select(a => a.Id).ToHashSet();
            var liabilityPayments = await db.LiabilityPayments
                .Where(p => p.Date <= monthEnd &&
                    (accountIds.Contains(p.AssetAccountId) || accountIds.Contains(p.LiabilityAccountId)))
                .ToListAsync();

            var paymentsByAsset = liabilityPayments
                .GroupBy(p => p.AssetAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var paymentsByLiability = liabilityPayments
                .GroupBy(p => p.LiabilityAccountId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            decimal assets = 0;
            decimal liabilities = 0;

            foreach (var account in accounts)
            {
                bool isLiability = account.AccountType.Name == "Liability";

                var balance = account.Transactions.Sum(t =>
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

            result.Add(new
            {
                month       = monthLabel.ToString("yyyy-MM"),
                assets      = Math.Round(assets, 2),
                liabilities = Math.Round(liabilities, 2),
                netWorth    = Math.Round(assets - liabilities, 2)
            });
        }

        return Ok(result);
    }

    [HttpGet("income-expense")]
    public async Task<IActionResult> GetIncomeExpense()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = new List<object>();

        for (int i = 5; i >= 0; i--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var transactions = await db.Transactions
                .Where(t => t.Date >= monthStart && t.Date <= monthEnd &&
                            t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
                .Include(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
                .ToListAsync();

            var income   = transactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
            var expenses = transactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

            result.Add(new
            {
                month    = monthStart.ToString("yyyy-MM"),
                income   = Math.Round(income, 2),
                expenses = Math.Round(expenses, 2)
            });
        }

        return Ok(result);
    }

    [HttpGet("spending-by-category")]
    public async Task<IActionResult> GetSpendingByCategory()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var result = await db.Transactions
            .Where(t => t.Date >= monthStart && t.Date <= today &&
                        t.Account.CurrencyId == currencyId &&
                        t.Category.CategoryType.Name == "Expense" &&
                        !t.Category.IsSystem)
            .Include(t => t.Category)
                .ThenInclude(c => c.CategoryType)
            .GroupBy(t => t.Category.Name)
            .Select(g => new
            {
                categoryName = g.Key,
                amount       = Math.Round(g.Sum(t => t.Amount), 2)
            })
            .OrderByDescending(x => x.amount)
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("account-balances")]
    public async Task<IActionResult> GetAccountBalances()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var accounts = await db.Accounts
            .Where(a => a.IsActive && a.CurrencyId == currencyId && a.AccountType.Name != "Liability")
            .Include(a => a.AccountType)
            .Include(a => a.Transactions)
                .ThenInclude(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
            .ToListAsync();

        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var liabilityPayments = await db.LiabilityPayments
            .Where(p => accountIds.Contains(p.AssetAccountId))
            .ToListAsync();

        var paymentsByAsset = liabilityPayments
            .GroupBy(p => p.AssetAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var result = accounts.Select(account =>
        {
            var balance = account.Transactions.Sum(t =>
            {
                if (t.Category.IsSystem) return t.Amount;
                bool isIncome = t.Category.CategoryType.Name == "Income";
                return isIncome ? t.Amount : -t.Amount;
            });
            balance -= paymentsByAsset.GetValueOrDefault(account.Id);

            return new
            {
                accountName = account.Name,
                balance     = Math.Round(balance, 2)
            };
        })
        .OrderByDescending(x => x.balance)
        .ToList();

        return Ok(result);
    }

    [HttpGet("cash-flow")]
    public async Task<IActionResult> GetCashFlow()
    {
        var settings = await settingsService.GetAsync();
        var currencyId = settings.DefaultCurrencyId;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = new List<object>();

        for (int i = 5; i >= 0; i--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd > today) monthEnd = today;

            var transactions = await db.Transactions
                .Where(t => t.Date >= monthStart && t.Date <= monthEnd &&
                            t.Account.CurrencyId == currencyId && !t.Category.IsSystem)
                .Include(t => t.Category)
                    .ThenInclude(c => c.CategoryType)
                .ToListAsync();

            var income   = transactions.Where(t => t.Category.CategoryType.Name == "Income").Sum(t => t.Amount);
            var expenses = transactions.Where(t => t.Category.CategoryType.Name == "Expense").Sum(t => t.Amount);

            result.Add(new
            {
                month   = monthStart.ToString("yyyy-MM"),
                netFlow = Math.Round(income - expenses, 2)
            });
        }

        return Ok(result);
    }
}
```

- [ ] **Step 4: Run the failing test again and confirm it now passes**

```bash
dotnet test --filter "GetNetWorthTrend_Returns200_WithExpectedShape"
```

Expected: PASS.

- [ ] **Step 5: Run full test suite to confirm no regressions**

```bash
dotnet test
```

Expected: all 309 tests pass (0 failed).

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Controllers/Api/DashboardApiController.cs \
        ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "feat: add net-worth-trend API endpoint with integration test"
```

---

## Task 2: Remaining 4 API endpoint tests

**Context:** The endpoint code was written in full in Task 1. Now write the 4 remaining integration tests and confirm they all pass.

**Files:**
- Modify: `ProjectCeres.Tests/Integration/DashboardApiTests.cs`

- [ ] **Step 1: Append the 4 remaining tests to DashboardApiTests**

Open `ProjectCeres.Tests/Integration/DashboardApiTests.cs` and append these tests to the class body:

```csharp
[Fact]
public async Task GetIncomeExpense_Returns200_WithExpectedShape()
{
    var response = await _client.GetAsync("/api/dashboard/income-expense");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Array);
    body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessOrEqualTo(6);

    foreach (var item in body.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("income", out _).Should().BeTrue();
        item.TryGetProperty("expenses", out _).Should().BeTrue();
    }
}

[Fact]
public async Task GetSpendingByCategory_Returns200_WithExpectedShape()
{
    var response = await _client.GetAsync("/api/dashboard/spending-by-category");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Array);

    foreach (var item in body.EnumerateArray())
    {
        item.TryGetProperty("categoryName", out _).Should().BeTrue();
        item.TryGetProperty("amount", out _).Should().BeTrue();
    }
}

[Fact]
public async Task GetAccountBalances_Returns200_WithExpectedShape()
{
    var response = await _client.GetAsync("/api/dashboard/account-balances");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Array);

    foreach (var item in body.EnumerateArray())
    {
        item.TryGetProperty("accountName", out _).Should().BeTrue();
        item.TryGetProperty("balance", out _).Should().BeTrue();
    }
}

[Fact]
public async Task GetCashFlow_Returns200_WithExpectedShape()
{
    var response = await _client.GetAsync("/api/dashboard/cash-flow");

    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.ValueKind.Should().Be(JsonValueKind.Array);
    body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessOrEqualTo(6);

    foreach (var item in body.EnumerateArray())
    {
        item.TryGetProperty("month", out _).Should().BeTrue();
        item.TryGetProperty("netFlow", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run all dashboard API tests**

```bash
dotnet test --filter "DashboardApiTests"
```

Expected: all 7 tests pass (2 existing + 5 new).

- [ ] **Step 3: Run full suite to confirm no regressions**

```bash
dotnet test
```

Expected: 314 tests pass, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Tests/Integration/DashboardApiTests.cs
git commit -m "test: add integration tests for 4 remaining dashboard chart endpoints"
```

---

## Task 3: NetWorthChart component

**Files:**
- Create: `ProjectCeres.Client/src/components/NetWorthChart.test.tsx`
- Create: `ProjectCeres.Client/src/components/NetWorthChart.tsx`

- [ ] **Step 1: Write the failing Vitest smoke test**

Create `ProjectCeres.Client/src/components/NetWorthChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { NetWorthChart } from './NetWorthChart'

describe('NetWorthChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', assets: 12000, liabilities: 3000, netWorth: 9000 },
        { month: '2025-12', assets: 12500, liabilities: 2800, netWorth: 9700 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<NetWorthChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<NetWorthChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd ProjectCeres.Client && pnpm test -- NetWorthChart
```

Expected: FAIL — `NetWorthChart` module not found.

- [ ] **Step 3: Implement NetWorthChart**

Create `ProjectCeres.Client/src/components/NetWorthChart.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { LineChart, Line, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface NetWorthPoint {
  month: string
  assets: number
  liabilities: number
  netWorth: number
}

export function NetWorthChart() {
  const [data, setData] = useState<NetWorthPoint[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/net-worth-trend')
      .then(r => r.json())
      .then((d: NetWorthPoint[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Net Worth Over Time</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No data available.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={data}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Legend />
              <Line type="monotone" dataKey="assets" stroke="#3b82f6" name="Assets" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="netWorth" stroke="#22c55e" name="Net Worth" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
cd ProjectCeres.Client && pnpm test -- NetWorthChart
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd .. && git add ProjectCeres.Client/src/components/NetWorthChart.tsx \
                 ProjectCeres.Client/src/components/NetWorthChart.test.tsx
git commit -m "feat: add NetWorthChart component with Vitest smoke test"
```

---

## Task 4: IncomeExpenseChart component

**Files:**
- Create: `ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx`
- Create: `ProjectCeres.Client/src/components/IncomeExpenseChart.tsx`

- [ ] **Step 1: Write the failing Vitest smoke test**

Create `ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { IncomeExpenseChart } from './IncomeExpenseChart'

describe('IncomeExpenseChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', income: 3500, expenses: 2100 },
        { month: '2025-12', income: 3200, expenses: 1900 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<IncomeExpenseChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<IncomeExpenseChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd ProjectCeres.Client && pnpm test -- IncomeExpenseChart
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement IncomeExpenseChart**

Create `ProjectCeres.Client/src/components/IncomeExpenseChart.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, Legend, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface IncomeExpensePoint {
  month: string
  income: number
  expenses: number
}

export function IncomeExpenseChart() {
  const [data, setData] = useState<IncomeExpensePoint[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/income-expense')
      .then(r => r.json())
      .then((d: IncomeExpensePoint[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Income vs. Expenses</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No data available.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Legend />
              <Bar dataKey="income" name="Income" fill="#22c55e" radius={[4, 4, 0, 0]} />
              <Bar dataKey="expenses" name="Expenses" fill="#ef4444" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
cd ProjectCeres.Client && pnpm test -- IncomeExpenseChart
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd .. && git add ProjectCeres.Client/src/components/IncomeExpenseChart.tsx \
                 ProjectCeres.Client/src/components/IncomeExpenseChart.test.tsx
git commit -m "feat: add IncomeExpenseChart component with Vitest smoke test"
```

---

## Task 5: SpendingDonutChart component

**Files:**
- Create: `ProjectCeres.Client/src/components/SpendingDonutChart.test.tsx`
- Create: `ProjectCeres.Client/src/components/SpendingDonutChart.tsx`

- [ ] **Step 1: Write the failing Vitest smoke test**

Create `ProjectCeres.Client/src/components/SpendingDonutChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { SpendingDonutChart } from './SpendingDonutChart'

describe('SpendingDonutChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { categoryName: 'Groceries', amount: 420 },
        { categoryName: 'Housing', amount: 850 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<SpendingDonutChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<SpendingDonutChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd ProjectCeres.Client && pnpm test -- SpendingDonutChart
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement SpendingDonutChart**

Create `ProjectCeres.Client/src/components/SpendingDonutChart.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface SpendingSlice {
  categoryName: string
  amount: number
}

const COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#ef4444', '#3b82f6', '#ec4899', '#14b8a6', '#f97316']

export function SpendingDonutChart() {
  const [data, setData] = useState<SpendingSlice[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/spending-by-category')
      .then(r => r.json())
      .then((d: SpendingSlice[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Spending by Category</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No expenses this month.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <PieChart>
              <Pie
                data={data}
                dataKey="amount"
                nameKey="categoryName"
                innerRadius={55}
                outerRadius={90}
                paddingAngle={2}
              >
                {data.map((_, index) => (
                  <Cell key={index} fill={COLORS[index % COLORS.length]} />
                ))}
              </Pie>
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Legend />
            </PieChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
cd ProjectCeres.Client && pnpm test -- SpendingDonutChart
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd .. && git add ProjectCeres.Client/src/components/SpendingDonutChart.tsx \
                 ProjectCeres.Client/src/components/SpendingDonutChart.test.tsx
git commit -m "feat: add SpendingDonutChart component with Vitest smoke test"
```

---

## Task 6: AccountBalancesChart component

**Files:**
- Create: `ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx`
- Create: `ProjectCeres.Client/src/components/AccountBalancesChart.tsx`

- [ ] **Step 1: Write the failing Vitest smoke test**

Create `ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { AccountBalancesChart } from './AccountBalancesChart'

describe('AccountBalancesChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { accountName: 'Checking', balance: 4200 },
        { accountName: 'Savings', balance: 8500 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<AccountBalancesChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<AccountBalancesChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd ProjectCeres.Client && pnpm test -- AccountBalancesChart
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement AccountBalancesChart**

Create `ProjectCeres.Client/src/components/AccountBalancesChart.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface AccountBalance {
  accountName: string
  balance: number
}

export function AccountBalancesChart() {
  const [data, setData] = useState<AccountBalance[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/account-balances')
      .then(r => r.json())
      .then((d: AccountBalance[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Account Balances</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No accounts found.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={Math.max(180, data.length * 40)}>
            <BarChart data={data} layout="vertical">
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="accountName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <Bar dataKey="balance" name="Balance" radius={[0, 4, 4, 0]}>
                {data.map((item, index) => (
                  <Cell key={index} fill={item.balance >= 0 ? '#3b82f6' : '#ef4444'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
cd ProjectCeres.Client && pnpm test -- AccountBalancesChart
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd .. && git add ProjectCeres.Client/src/components/AccountBalancesChart.tsx \
                 ProjectCeres.Client/src/components/AccountBalancesChart.test.tsx
git commit -m "feat: add AccountBalancesChart component with Vitest smoke test"
```

---

## Task 7: CashFlowChart component

**Files:**
- Create: `ProjectCeres.Client/src/components/CashFlowChart.test.tsx`
- Create: `ProjectCeres.Client/src/components/CashFlowChart.tsx`

- [ ] **Step 1: Write the failing Vitest smoke test**

Create `ProjectCeres.Client/src/components/CashFlowChart.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { CashFlowChart } from './CashFlowChart'

describe('CashFlowChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', netFlow: 1400 },
        { month: '2025-12', netFlow: -200 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<CashFlowChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<CashFlowChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
```

- [ ] **Step 2: Run the test and confirm it fails**

```bash
cd ProjectCeres.Client && pnpm test -- CashFlowChart
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement CashFlowChart**

Create `ProjectCeres.Client/src/components/CashFlowChart.tsx`:

```tsx
import { useEffect, useState } from 'react'
import { BarChart, Bar, XAxis, YAxis, Tooltip, ReferenceLine, Cell, ResponsiveContainer } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

interface CashFlowPoint {
  month: string
  netFlow: number
}

export function CashFlowChart() {
  const [data, setData] = useState<CashFlowPoint[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    fetch('/api/dashboard/cash-flow')
      .then(r => r.json())
      .then((d: CashFlowPoint[]) => { setData(d); setLoading(false) })
      .catch(() => setLoading(false))
  }, [])

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">Monthly Cash Flow</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading…</p>}
        {!loading && data.length === 0 && (
          <p className="text-sm text-muted-foreground">No data available.</p>
        )}
        {!loading && data.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip formatter={(value: number) => value.toFixed(2)} />
              <ReferenceLine y={0} stroke="#94a3b8" />
              <Bar dataKey="netFlow" name="Net Flow" radius={[4, 4, 0, 0]}>
                {data.map((item, index) => (
                  <Cell key={index} fill={item.netFlow >= 0 ? '#22c55e' : '#ef4444'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  )
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
cd ProjectCeres.Client && pnpm test -- CashFlowChart
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd .. && git add ProjectCeres.Client/src/components/CashFlowChart.tsx \
                 ProjectCeres.Client/src/components/CashFlowChart.test.tsx
git commit -m "feat: add CashFlowChart component with Vitest smoke test"
```

---

## Task 8: Wire components into Dashboard view and main.tsx

**Files:**
- Modify: `ProjectCeres/Views/Dashboard/Index.cshtml`
- Modify: `ProjectCeres.Client/src/main.tsx`

- [ ] **Step 1: Add chart mount points to the Dashboard Razor view**

Open `ProjectCeres/Views/Dashboard/Index.cshtml`. After the existing budget bars `<div>` (the `grid grid-cols-1 md:grid-cols-2 gap-6 mb-8` block containing `data-react="category-budget-bars"` and `data-react="goal-budget-bars"`), append:

```html
<div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-6">
    <div data-react="net-worth-chart"></div>
    <div data-react="income-expense-chart"></div>
</div>

<div class="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
    <div data-react="spending-donut-chart"></div>
    <div data-react="account-balances-chart"></div>
    <div data-react="cash-flow-chart"></div>
</div>
```

- [ ] **Step 2: Add mount blocks to main.tsx**

Open `ProjectCeres.Client/src/main.tsx`. Add imports at the top with the other component imports:

```tsx
import { NetWorthChart } from './components/NetWorthChart'
import { IncomeExpenseChart } from './components/IncomeExpenseChart'
import { SpendingDonutChart } from './components/SpendingDonutChart'
import { AccountBalancesChart } from './components/AccountBalancesChart'
import { CashFlowChart } from './components/CashFlowChart'
```

Then append these mount blocks at the bottom of the file (after the existing `goalBudgetBarsEl` block):

```tsx
const netWorthChartEl = document.querySelector<HTMLElement>('[data-react="net-worth-chart"]')
if (netWorthChartEl) {
  createRoot(netWorthChartEl).render(
    <StrictMode>
      <NetWorthChart />
    </StrictMode>,
  )
}

const incomeExpenseChartEl = document.querySelector<HTMLElement>('[data-react="income-expense-chart"]')
if (incomeExpenseChartEl) {
  createRoot(incomeExpenseChartEl).render(
    <StrictMode>
      <IncomeExpenseChart />
    </StrictMode>,
  )
}

const spendingDonutChartEl = document.querySelector<HTMLElement>('[data-react="spending-donut-chart"]')
if (spendingDonutChartEl) {
  createRoot(spendingDonutChartEl).render(
    <StrictMode>
      <SpendingDonutChart />
    </StrictMode>,
  )
}

const accountBalancesChartEl = document.querySelector<HTMLElement>('[data-react="account-balances-chart"]')
if (accountBalancesChartEl) {
  createRoot(accountBalancesChartEl).render(
    <StrictMode>
      <AccountBalancesChart />
    </StrictMode>,
  )
}

const cashFlowChartEl = document.querySelector<HTMLElement>('[data-react="cash-flow-chart"]')
if (cashFlowChartEl) {
  createRoot(cashFlowChartEl).render(
    <StrictMode>
      <CashFlowChart />
    </StrictMode>,
  )
}
```

- [ ] **Step 3: Run full Vitest suite**

```bash
cd ProjectCeres.Client && pnpm test
```

Expected: all tests pass (0 failed).

- [ ] **Step 4: Run full dotnet test suite**

```bash
cd <repo> && dotnet test
```

Expected: 314 tests pass, 0 failed.

- [ ] **Step 5: Build the client bundle**

```bash
cd ProjectCeres.Client && pnpm build
```

Expected: build succeeds with 0 TypeScript errors.

- [ ] **Step 6: Commit**

```bash
cd .. && git add ProjectCeres/Views/Dashboard/Index.cshtml \
                 ProjectCeres.Client/src/main.tsx
git commit -m "feat: wire 5 chart components into dashboard view and main.tsx"
```

---

## Task 9: Final verification

- [ ] **Step 1: Start the app and open the dashboard in a browser**

```bash
dotnet run --project ProjectCeres
```

Open `https://localhost:5001` (or the port shown in the console). Navigate to the Dashboard.

- [ ] **Step 2: Verify all 5 charts render**

Check each chart renders (no blank areas, no console errors):
- Net Worth Over Time — line chart with Assets and Net Worth lines
- Income vs. Expenses — grouped bar chart with green/red bars
- Spending by Category — doughnut with colored slices
- Account Balances — horizontal bar chart
- Monthly Cash Flow — bar chart, negative months red

Open browser DevTools → Console: **0 errors**.

- [ ] **Step 3: Run both test suites one final time**

```bash
dotnet test
cd ProjectCeres.Client && pnpm test
```

Expected: `dotnet test` → 314 passed, 0 failed. `pnpm test` → all Vitest tests passed.

- [ ] **Step 4: Update the roadmap verification checklist**

Open `docs/roadmap-phase-two.md`. In the **Visual Dashboard** section, mark these items `[x]`:

```markdown
- [x] Dashboard loads all seven chart components; no console errors
- [x] Net Worth Over Time chart: points correspond to known account balances at end of each month
- [x] Income vs. Expenses chart: bars match MTD totals shown in the text dashboard
- [x] Spending by Category donut: slices sum to total expense amount for the current month
- [x] Account Balances chart: all active accounts shown with correct balances
- [x] Category Budget Progress bars: `spent` and `limit` values match `BudgetService.GetActualSpendAsync` output
- [x] Goal Budget Progress bars: `Spending` goals reflect tagged transaction totals; `Savings` goals reflect account balances
- [x] Monthly Cash Flow chart: net values (income − expenses) correct per month
```

- [ ] **Step 5: Final commit**

```bash
git add docs/roadmap-phase-two.md
git commit -m "docs: mark Stage 8.2 dashboard charts complete in roadmap checklist"
```
