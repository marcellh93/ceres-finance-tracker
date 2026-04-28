# Layered Spendable Balance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single Spendable Balance figure with two layered numbers — *Available Today* (liquid minus imminent bills) and *Safe to Spend* (further minus later bills and budget reserve) — and fix the missing margin between the Financial Health card and the budget section.

**Architecture:** Expand `HealthSnapshotData` with four new fields (`AvailableToday`, `SafeToSpend`, `ImminentBills`, `LaterBills`, `BudgetReserve`). The existing `SpendableBalance` field is repurposed as `AvailableToday` (rename in record + all callers). `GetSpendableBalanceAsync` is split into helpers that feed both numbers. The `_HealthSnapshot.cshtml` partial is redesigned: the spendable cell becomes a hero showing both figures with an inline breakdown. A `mb-6` gap is added in `Index.cshtml` between the health card and the budget grid.

**Tech Stack:** C# / ASP.NET Core / Razor / Tailwind CSS v3 / xUnit + FluentAssertions

---

## File Map

| File | Change |
|------|--------|
| `ProjectCeres/Services/IDashboardService.cs` | Rename `SpendableBalance` → `AvailableToday`; add `SafeToSpend`, `ImminentBills`, `LaterBills`, `BudgetReserve` to `HealthSnapshotData` |
| `ProjectCeres/Services/DashboardService.cs` | Rewrite `GetSpendableBalanceAsync` to return the full breakdown; compute `LaterBills` and `BudgetReserve` |
| `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml` | Redesign spendable cell; render both figures + breakdown |
| `ProjectCeres/Views/Dashboard/Index.cshtml` | Add `mb-6` between health partial and budget grid |
| `ProjectCeres.Tests/Integration/DashboardServiceTests.cs` | Update existing spendable tests (field rename); add tests for `SafeToSpend`, `LaterBills`, `BudgetReserve` |
| `docs/decisions/ADR-0049-financial-health-snapshot.md` | Update Spendable Balance formula section |
| `docs/decisions/ADR-0062-financial-health-metrics.md` | Update Metric 1 definition, record shape, and UI rendering notes |

---

## Task 1: Expand `HealthSnapshotData` and rename `SpendableBalance`

**Files:**
- Modify: `ProjectCeres/Services/IDashboardService.cs`

- [ ] **Step 1: Replace the record definition**

Open `ProjectCeres/Services/IDashboardService.cs`. Replace the `HealthSnapshotData` record (lines 12–20) with:

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

- [ ] **Step 2: Build to find all call-sites that reference `SpendableBalance`**

```bash
dotnet build ProjectCeres 2>&1 | grep -E "error|SpendableBalance"
```

Expected: build errors at `DashboardService.cs` (construction site) and `_HealthSnapshot.cshtml` (render site). These are fixed in Tasks 2 and 4.

---

## Task 2: Rewrite `GetSpendableBalanceAsync` in `DashboardService`

**Files:**
- Modify: `ProjectCeres/Services/DashboardService.cs`

The existing method returns a single `decimal?`. It needs to return a tuple with all five components so `GetHealthSnapshotAsync` can populate the expanded record.

- [ ] **Step 1: Write the failing tests first** (see Task 3 — write tests before touching production code)

*(This task depends on Task 3 tests existing and failing. Come back here after Task 3 Step 1–3.)*

- [ ] **Step 2: Update the private helper signature**

In `DashboardService.cs`, replace the signature of `GetSpendableBalanceAsync`:

```csharp
private async Task<(decimal? availableToday, decimal? safeToSpend, decimal? imminentBills, decimal? laterBills, decimal? budgetReserve)> GetSpendableBalanceAsync(int currencyId)
```

- [ ] **Step 3: Rewrite the method body**

Replace the full body of `GetSpendableBalanceAsync` with:

```csharp
{
    var today    = DateOnly.FromDateTime(DateTime.Today);
    var firstDay = new DateOnly(today.Year, today.Month, 1);
    var lastDay  = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
    const int imminentWindowDays = 7;

    // Load active, non-excluded asset accounts with their transactions and category types.
    var accounts = await db.Accounts
        .Where(a => a.IsActive
                 && a.CurrencyId == currencyId
                 && a.AccountType.Name == "Asset"
                 && !a.ExcludeFromSpendable)
        .Include(a => a.Transactions)
            .ThenInclude(t => t.Category)
                .ThenInclude(c => c.CategoryType)
        .ToListAsync();

    if (accounts.Count == 0)
        return (null, null, null, null, null);

    // Derive liquid balance per account (same sign logic as AccountService).
    decimal liquid = 0m;
    foreach (var account in accounts)
    {
        liquid += account.Transactions.Sum(t =>
        {
            if (t.Category.IsSystem) return t.Amount;
            bool isIncome = t.Category.CategoryType.Name == "Income";
            return isIncome ? t.Amount : -t.Amount;
        });
    }

    // Subtract liability payments that source from these asset accounts.
    var accountIds = accounts.Select(a => a.Id).ToHashSet();
    var liabilityPayments = await db.LiabilityPayments
        .AsNoTracking()
        .Where(p => accountIds.Contains(p.AssetAccountId))
        .ToListAsync();
    liquid -= liabilityPayments.Sum(p => p.Amount);

    // Include transfers (same fix applied to the single-number version).
    var transfersIn = await db.Transfers
        .Where(t => accountIds.Contains(t.DestAccountId))
        .SumAsync(t => (decimal?)t.Amount) ?? 0m;
    var transfersOut = await db.Transfers
        .Where(t => accountIds.Contains(t.SourceAccountId))
        .SumAsync(t => (decimal?)t.Amount) ?? 0m;
    liquid += transfersIn;
    liquid -= transfersOut;

    // Split recurring transactions due this month into imminent (≤7 days) and later (>7 days).
    // Overdue (NextDueDate < today) is bucketed as imminent — more urgent, not less.
    var dueRecurring = await db.RecurringTransactions
        .Where(r => r.IsActive
                 && r.EstimatedAmount != null
                 && r.NextDueDate >= firstDay
                 && r.NextDueDate <= lastDay
                 && accountIds.Contains(r.AccountId))
        .ToListAsync();

    var imminentCutoff = today.AddDays(imminentWindowDays);
    decimal imminentBills = dueRecurring
        .Where(r => r.NextDueDate <= imminentCutoff)
        .Sum(r => r.EstimatedAmount ?? 0m);
    decimal laterBills = dueRecurring
        .Where(r => r.NextDueDate > imminentCutoff)
        .Sum(r => r.EstimatedAmount ?? 0m);

    // Budget reserve = SUM(MAX(0, limit − actual spend this month)) per active CategoryBudget.
    var activeBudgets = await db.CategoryBudgets
        .Where(cb => cb.IsActive && cb.CurrencyId == currencyId)
        .ToListAsync();

    decimal budgetReserve = 0m;
    if (activeBudgets.Count > 0)
    {
        var budgetCategoryIds = activeBudgets.Select(cb => cb.CategoryId).ToList();
        var actualSpendByCategory = await db.Transactions
            .Where(t => t.Date >= firstDay
                     && t.Date <= today
                     && budgetCategoryIds.Contains(t.CategoryId)
                     && t.Account.CurrencyId == currencyId)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync();

        var spendMap = actualSpendByCategory.ToDictionary(x => x.CategoryId, x => x.Total);
        foreach (var budget in activeBudgets)
        {
            var actual  = spendMap.GetValueOrDefault(budget.CategoryId);
            var reserve = budget.LimitAmount - actual;
            if (reserve > 0)
                budgetReserve += reserve;
        }
    }

    var availableToday = liquid - imminentBills;
    var safeToSpend    = availableToday - laterBills - budgetReserve;

    return (availableToday, safeToSpend, imminentBills, laterBills, budgetReserve);
}
```

- [ ] **Step 4: Update `GetHealthSnapshotAsync` to use the new tuple**

In `GetHealthSnapshotAsync`, replace:
```csharp
var spendable    = await GetSpendableBalanceAsync(currencyId);
```
with:
```csharp
var (availableToday, safeToSpend, imminentBills, laterBills, budgetReserve) = await GetSpendableBalanceAsync(currencyId);
```

And replace the `return new HealthSnapshotData(...)` constructor call to use the new fields:
```csharp
return new HealthSnapshotData(
    AvailableToday:       availableToday,
    SafeToSpend:          safeToSpend,
    ImminentBills:        imminentBills,
    LaterBills:           laterBills,
    BudgetReserve:        budgetReserve,
    RunwayMonths:         runway,
    CurrentMonthIncome:   incomeMetrics.currentMonth,
    RollingAverageIncome: incomeMetrics.rollingAverage,
    IncomeDeltaPercent:   incomeMetrics.deltaPercent,
    BudgetBurnRate:       burnRate,
    CurrencySymbol:       currency.Symbol,
    CurrencyCode:         currency.Code);
```

- [ ] **Step 5: Build to verify no remaining compilation errors**

```bash
dotnet build ProjectCeres 2>&1 | grep -E "^.*error"
```

Expected: only `_HealthSnapshot.cshtml` still failing (fixed in Task 4). No C# errors.

---

## Task 3: Update and add tests for the new formula

**Files:**
- Modify: `ProjectCeres.Tests/Integration/DashboardServiceTests.cs`

**Do this BEFORE Task 2 Step 1** — write tests first, watch them fail, then implement.

- [ ] **Step 1: Rename `SpendableBalance` → `AvailableToday` in all existing spendable tests**

Find every occurrence of `.SpendableBalance` in the test file and replace with `.AvailableToday`. There are four: in `ExcludesExcludedAccountAndSubtractsDueRecurring`, `NextMonthRecurringNotSubtracted`, and `ExcludesTransfersOutToExcludedAccounts`.

```bash
grep -n "SpendableBalance" ProjectCeres.Tests/Integration/DashboardServiceTests.cs
```

Replace each `snapshot.SpendableBalance` and `baselineSnapshot.SpendableBalance` with the new names.

- [ ] **Step 2: Run tests to confirm they fail with a compile error (field renamed)**

```bash
dotnet test ProjectCeres.Tests --filter "SpendableBalance" 2>&1 | tail -20
```

Expected: compilation error — `'HealthSnapshotData' does not contain a definition for 'SpendableBalance'`.

- [ ] **Step 3: Add test — SafeToSpend subtracts LaterBills and BudgetReserve**

Add this test after `GetHealthSnapshotAsync_SpendableBalance_NextMonthRecurringNotSubtracted`:

```csharp
[Fact]
public async Task GetHealthSnapshotAsync_SafeToSpend_SubtractsLaterBillsAndBudgetReserve()
{
    // Deactivate any existing active CategoryBudgets for CurrencyId=1 to isolate this test
    var existingBudgets = _fixture.Db.CategoryBudgets.Where(cb => cb.IsActive && cb.CurrencyId == 1).ToList();
    foreach (var b in existingBudgets)
        b.IsActive = false;

    // Baseline
    var baseline = await _service.GetHealthSnapshotAsync();
    var baselineAvailable = baseline.AvailableToday ?? 0m;
    var baselineSafe      = baseline.SafeToSpend    ?? 0m;

    // Non-excluded account: 3000m income → liquid = 3000
    AddMtdTransaction(SalaryCategoryId, 3000m);

    // Recurring due in 3 days (imminent, within 7-day window) → subtracted from AvailableToday
    var today = DateOnly.FromDateTime(DateTime.Today);
    _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
    {
        Id              = Guid.NewGuid(),
        Name            = "Imminent Bill",
        EstimatedAmount = 400m,
        AccountId       = _accountId,
        CategoryId      = HousingCategoryId,
        Frequency       = Frequency.Monthly,
        NextDueDate     = today.AddDays(3),
        IsActive        = true
    });

    // Recurring due in 15 days (later, outside 7-day window) → NOT subtracted from AvailableToday
    _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
    {
        Id              = Guid.NewGuid(),
        Name            = "Later Bill",
        EstimatedAmount = 500m,
        AccountId       = _accountId,
        CategoryId      = HousingCategoryId,
        Frequency       = Frequency.Monthly,
        NextDueDate     = today.AddDays(15),
        IsActive        = true
    });

    // Active CategoryBudget with 200m limit and 50m actual spend this month
    _fixture.Db.CategoryBudgets.Add(new CategoryBudget
    {
        Id          = Guid.NewGuid(),
        CategoryId  = HousingCategoryId,
        CurrencyId  = 1,
        LimitAmount = 200m,
        IsActive    = true
    });
    AddMtdTransaction(HousingCategoryId, 50m);   // 50m spent → 150m reserve remaining

    await _fixture.Db.SaveChangesAsync();

    var snapshot = await _service.GetHealthSnapshotAsync();

    // liquid = baseline + 3000 - 50 (expense) = baseline + 2950
    // AvailableToday = liquid - imminentBills(400) = baseline + 2950 - 400 = baseline + 2550
    // LaterBills = 500
    // BudgetReserve = MAX(0, 200 - 50) = 150
    // SafeToSpend = AvailableToday - LaterBills - BudgetReserve = baseline + 2550 - 500 - 150 = baseline + 1900
    snapshot.AvailableToday.Should().Be(baselineAvailable + 2550m);
    snapshot.SafeToSpend.Should().Be(baselineSafe + 1900m);
    snapshot.LaterBills.Should().BeGreaterThanOrEqualTo(500m);
    snapshot.BudgetReserve.Should().BeGreaterThanOrEqualTo(150m);
}
```

- [ ] **Step 4: Add test — overdue recurring is treated as imminent**

```csharp
[Fact]
public async Task GetHealthSnapshotAsync_AvailableToday_TreatsOverdueRecurringAsImminent()
{
    var baseline = (await _service.GetHealthSnapshotAsync()).AvailableToday ?? 0m;

    AddMtdTransaction(SalaryCategoryId, 1000m);

    // Recurring that was due yesterday (overdue) — must be subtracted from AvailableToday
    var today = DateOnly.FromDateTime(DateTime.Today);
    var firstDay = new DateOnly(today.Year, today.Month, 1);
    // Use first day of month so it's within the current-month window and before today
    _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
    {
        Id              = Guid.NewGuid(),
        Name            = "Overdue Bill",
        EstimatedAmount = 300m,
        AccountId       = _accountId,
        CategoryId      = HousingCategoryId,
        Frequency       = Frequency.Monthly,
        NextDueDate     = firstDay,   // always in range; always <= today
        IsActive        = true
    });
    await _fixture.Db.SaveChangesAsync();

    var snapshot = await _service.GetHealthSnapshotAsync();

    // Overdue bills count as imminent → AvailableToday = baseline + 1000 - 300 = baseline + 700
    snapshot.AvailableToday.Should().Be(baseline + 700m);
    snapshot.ImminentBills.Should().BeGreaterThanOrEqualTo(300m);
}
```

- [ ] **Step 5: Run all new and existing tests to confirm they fail for the right reason**

```bash
dotnet test ProjectCeres.Tests --filter "DashboardServiceTests" 2>&1 | tail -30
```

Expected: compile errors or assertion failures showing field-not-found or wrong values — not test setup errors.

*(Now implement Task 2 to make them pass.)*

- [ ] **Step 6: After Task 2 is complete, run all dashboard tests and confirm green**

```bash
dotnet test ProjectCeres.Tests --filter "DashboardServiceTests" 2>&1 | tail -15
```

Expected: all tests pass, output is clean.

- [ ] **Step 7: Run full suite to confirm no regressions**

```bash
dotnet test ProjectCeres.Tests 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0`.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Services/IDashboardService.cs \
        ProjectCeres/Services/DashboardService.cs \
        ProjectCeres.Tests/Integration/DashboardServiceTests.cs
git commit -m "feat(health): layered spendable balance — AvailableToday + SafeToSpend"
```

---

## Task 4: Redesign `_HealthSnapshot.cshtml` — spendable cell hero + gap fix

**Files:**
- Modify: `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml`
- Modify: `ProjectCeres/Views/Dashboard/Index.cshtml`

- [ ] **Step 1: Fix the gap in `Index.cshtml`**

In `ProjectCeres/Views/Dashboard/Index.cshtml`, find:

```cshtml
@await Html.PartialAsync("_HealthSnapshot")

<div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
```

Replace with:

```cshtml
@await Html.PartialAsync("_HealthSnapshot")

<div class="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8 mt-6">
```

*(The `mb-8` on the health card's outer wrapper already provides bottom margin, but `mt-6` on the following grid creates a reliable gap between them.)*

- [ ] **Step 2: Redesign the spendable cell in `_HealthSnapshot.cshtml`**

Replace the entire first `<div>` block inside the 4-column grid (the `@* — Spendable Balance — *@` section, lines 13–26) with:

```cshtml
@* — Available Today / Safe to Spend — *@
<div class="p-[14px_18px] border-r border-gray-200 @(snapshot.AvailableToday == null ? "bg-gray-50" : "")">
    <div class="text-[10.5px] uppercase tracking-widest text-gray-400 font-medium mb-2">Spendable Balance</div>
    @if (snapshot.AvailableToday == null)
    {
        <div class="text-[11.5px] italic text-gray-400 leading-snug">No asset accounts found</div>
    }
    else
    {
        @* Available Today — headline number *@
        <div class="text-lg font-bold @(snapshot.AvailableToday >= 0 ? "text-green-600" : "text-red-600") leading-tight"
             style="font-family:'IBM Plex Mono',ui-monospace,monospace">
            @snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.AvailableToday.Value, ViewData["NumberFormat"] as string)
        </div>
        <div class="text-[10px] text-gray-400 uppercase tracking-wider mt-0.5 mb-3">Available today</div>

        @* Safe to Spend — subdued secondary number *@
        @if (snapshot.SafeToSpend != null)
        {
            var safeClass = snapshot.SafeToSpend >= 0 ? "text-gray-700" : "text-amber-600";
            <div class="text-sm font-semibold @safeClass leading-tight"
                 style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                @snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.SafeToSpend.Value, ViewData["NumberFormat"] as string)
            </div>
            <div class="text-[10px] text-gray-400 uppercase tracking-wider mt-0.5">Safe to spend</div>
        }

        @* Breakdown — only shown when at least one deduction is non-zero *@
        @{
            bool hasDeductions = (snapshot.ImminentBills ?? 0m) != 0m
                              || (snapshot.LaterBills    ?? 0m) != 0m
                              || (snapshot.BudgetReserve ?? 0m) != 0m;
        }
        @if (hasDeductions)
        {
            <div class="mt-3 pt-2 border-t border-gray-100 space-y-0.5">
                @if ((snapshot.ImminentBills ?? 0m) != 0m)
                {
                    <div class="flex justify-between text-[10.5px] text-gray-500">
                        <span>Bills due (7 days)</span>
                        <span class="font-medium" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                            −@snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.ImminentBills!.Value, ViewData["NumberFormat"] as string)
                        </span>
                    </div>
                }
                @if ((snapshot.LaterBills ?? 0m) != 0m)
                {
                    <div class="flex justify-between text-[10.5px] text-gray-500">
                        <span>Bills later this month</span>
                        <span class="font-medium" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                            −@snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.LaterBills!.Value, ViewData["NumberFormat"] as string)
                        </span>
                    </div>
                }
                @if ((snapshot.BudgetReserve ?? 0m) != 0m)
                {
                    <div class="flex justify-between text-[10.5px] text-gray-500">
                        <span>Budget remaining</span>
                        <span class="font-medium" style="font-family:'IBM Plex Mono',ui-monospace,monospace">
                            −@snapshot.CurrencySymbol @NumberFormatHelper.FormatAmount(snapshot.BudgetReserve!.Value, ViewData["NumberFormat"] as string)
                        </span>
                    </div>
                }
            </div>
        }
    }
</div>
```

- [ ] **Step 3: Build to verify no template errors**

```bash
dotnet build ProjectCeres 2>&1 | grep -E "error"
```

Expected: clean build.

- [ ] **Step 4: Start the app and verify visually**

```bash
dotnet run --project ProjectCeres
```

Open `http://localhost:5000` (or the displayed port). Confirm:
- Financial Health card shows "Spendable Balance" with two numbers: a large green *Available today* and a smaller *Safe to spend* below it
- The breakdown lines (Bills due 7 days, Bills later this month, Budget remaining) appear only when non-zero
- There is a visible gap between the Financial Health card and the Category Budgets / Goal Budgets section
- No visual regressions in the other three metric cells (Runway, Income vs. Avg, Budget Burn Rate)

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml \
        ProjectCeres/Views/Dashboard/Index.cshtml
git commit -m "feat(dashboard): layered spendable UI — Available Today + Safe to Spend breakdown"
```

---

## Task 5: Update documentation

**Files:**
- Modify: `docs/decisions/ADR-0049-financial-health-snapshot.md`
- Modify: `docs/decisions/ADR-0062-financial-health-metrics.md`

- [ ] **Step 1: Update ADR-0049**

In `docs/decisions/ADR-0049-financial-health-snapshot.md`, replace the "Three metrics" section header with "Four metrics" and add a new metric block before the existing Runway section:

Replace:
```markdown
### Three metrics
```
with:
```markdown
### Four metrics
```

After the new heading, insert the following before **1. Runway**:

```markdown
**0. Spendable Balance (two-tier)**

How much can the user spend right now vs. after all planned obligations?

```
Available Today = liquid balance of non-excluded asset accounts
                − recurring bills due within the next 7 days (or overdue)

Safe to Spend   = Available Today
                − recurring bills due 8–31 days from now (same calendar month)
                − budget reserve (SUM of MAX(0, limit − actual spend) per active CategoryBudget)
```

*Available Today* answers "can I buy this right now without missing a bill?"
*Safe to Spend* answers "can I buy this and stay on plan for the month?"

Both figures are displayed in the Financial Health card. *Available Today* is the headline. *Safe to Spend* is a smaller secondary line below it. A breakdown of deductions (bills due soon, bills later, budget reserve) is shown inline when any deduction is non-zero.

Edge cases:
- No qualifying asset accounts → both null (not shown)
- Safe to Spend < 0 → shown in amber, not red (it is a planning signal, not a crisis)
- Over-budget categories contribute zero to BudgetReserve (overspend is already reflected in the liquid balance)
```

- [ ] **Step 2: Update ADR-0062 — record shape**

In `docs/decisions/ADR-0062-financial-health-metrics.md`, replace the code block showing the `HealthSnapshotData` record:

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

with:

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

- [ ] **Step 3: Update ADR-0062 — Metric 1 formula**

In the `### Metric 1: Spendable Balance` section, replace the formula paragraph:

```
**Formula:** `SUM(balances of non-excluded asset accounts) − SUM(EstimatedAmount of qualifying recurring transactions due this calendar month)`
```

with:

```
**Formula (two-tier):**

```
AvailableToday = liquid − ImminentBills
SafeToSpend    = AvailableToday − LaterBills − BudgetReserve

liquid         = SUM(balances of non-excluded EUR asset accounts, including transfers)
ImminentBills  = SUM(EstimatedAmount of active recurring transactions whose NextDueDate
                     falls between firstDayOfMonth and today+7, inclusive; overdue also included)
LaterBills     = SUM(EstimatedAmount of active recurring transactions whose NextDueDate
                     falls between today+8 and lastDayOfMonth, inclusive)
BudgetReserve  = SUM(MAX(0, LimitAmount − actualSpendThisMonth) per active CategoryBudget)
```

**Imminent window:** 7 days. Catches genuinely urgent obligations (direct debits often post 1–2 business days early) without sweeping in mid-month items.

**Budget reserve is soft:** It reduces only `SafeToSpend`, never `AvailableToday`. Budgets are aspirational allocations; bills are obligations.

**Known double-count:** If a subscription has both a recurring entry and a CategoryBudget, it is subtracted twice from `SafeToSpend`. Accepted in Phase 2 — resolution requires linking RecurringTransaction to Category.

**Returns null when:** No qualifying (non-excluded) asset accounts exist (both tiers null).
```

- [ ] **Step 4: Update ADR-0062 — UI rendering note**

Find the line:
```
**UI rendering:** Null renders as `—` with an inline note "Not enough data" (`text-muted` class). Zero renders as the numeric value.
```

Replace with:
```
**UI rendering:** Null renders as "No asset accounts found" (italic, muted). `AvailableToday` is the headline number (large, green/red). `SafeToSpend` renders below it as a smaller secondary number (neutral gray or amber when negative). An inline breakdown (bills due soon, bills later, budget reserve) appears when at least one deduction is non-zero. All other null metrics render as an italic muted note.
```

- [ ] **Step 5: Commit docs**

```bash
git add docs/decisions/ADR-0049-financial-health-snapshot.md \
        docs/decisions/ADR-0062-financial-health-metrics.md
git commit -m "docs: update ADR-0049 and ADR-0062 for layered spendable balance"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All three requirements covered — `AvailableToday` (Task 2), `SafeToSpend` (Task 2), gap fix (Task 4 Step 1), UI redesign (Task 4 Step 2), docs (Task 5).
- [x] **Placeholder scan:** No TBD/TODO. All code blocks are complete.
- [x] **Type consistency:** `AvailableToday`, `SafeToSpend`, `ImminentBills`, `LaterBills`, `BudgetReserve` used identically across Tasks 1, 2, 3, 4.
- [x] **Known double-count:** Documented in ADR update and noted in Task 2 method comment.
- [x] **Test-first order:** Task 3 explicitly instructs writing tests before Task 2 implementation.
