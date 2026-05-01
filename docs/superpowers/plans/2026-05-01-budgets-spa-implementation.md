# Budgets SPA Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the SPA Budgets surface (Category + Goal Budgets, unified at `/app/budgets` with tabs), implement `Settings.BudgetPeriodStartDay` so dashboard cycle math respects user-configured start day, demolish Razor `BudgetsController` page actions to 302 redirects, sync planning docs.

**Architecture:** Two new API controllers (`CategoryBudgetsApiController`, `GoalBudgetsApiController`) and one tiny discriminator controller (`BudgetsApiController`). Server services gain a `currency` filter and an `includeInactive` overload, plus a new `BudgetPeriod` static helper for period-boundary math. `CategoryBudgetService.GetActualSpendAsync` keeps its `(year, month)` signature; semantics shift to "the period whose end falls in (year, month)." Settings table gains `BudgetPeriodStartDay int NOT NULL DEFAULT 1`. SPA gains a routed list page `/app/budgets` with tabs, routed Create at `/budgets/new?type=category|spending|savings`, routed Edit at `/budgets/:id/edit` resolved via the discriminator endpoint, plus Settings UI for the start day. Movement form gains a conditional Spending-Goal picker. Razor `BudgetsController` actions become 302s; all `Views/Budgets/*.cshtml` deleted; POST overloads deleted entirely.

**Tech Stack:** .NET 10 (EF Core, Npgsql), React 19 + Vite + TypeScript, shadcn/ui base-nova, sonner, react-router-dom v7, Vitest + Testing Library on the client, xUnit + FluentAssertions on the server.

**Spec:** `docs/superpowers/specs/2026-05-01-budgets-spa-design.md`. Read §1 (page surface), §3 (list rendering), §4 (forms), §5 (period semantics — most subtle part), §6 (archive lifecycle + 409 flow), §7 (Movement form Spending-Goal picker), §8 (15 endpoints across 3 controllers), §9 (Razor demolition).

**Conventions used throughout this plan:**

- **Tests:** Vitest + React Testing Library on the client; xUnit + FluentAssertions on the server. Server integration tests use the existing `TestWebApplicationFactory`.
- **Each task ends with one commit.** No amending.
- **Imports:** match existing client style (alphabetized within groups, `@/` aliases for shadcn primitives, relative paths for sibling modules).
- **Toasts:** `sonner` via `import { toast } from 'sonner'`.
- **No `Co-Authored-By:` trailer** in any commit.
- **Run pnpm + dotnet commands in the foreground.** Never backgrounded — pnpm subprocess buffering produces empty output.
- **Working directly on `main`.** No branches, no worktrees. Solo project.

---

## File Structure

**Created (server):**
- `ProjectCeres/Migrations/<timestamp>_AddSettingsBudgetPeriodStartDay.cs` — adds `BudgetPeriodStartDay` column to Settings.
- `ProjectCeres/Services/BudgetPeriod.cs` — pure static helper for period boundary math.
- `ProjectCeres/Services/BudgetPeriodTests.cs` placeholder file location is xUnit suite → goes under `ProjectCeres.Tests/Unit/Services/BudgetPeriodTests.cs`.
- `ProjectCeres/ViewModels/CategoryBudgetCrudDtos.cs` — DTO records for the API (list, create, update, edit, spend response).
- `ProjectCeres/ViewModels/GoalBudgetCrudDtos.cs` — DTO records for the API (list, create, update, edit, progress response).
- `ProjectCeres/ViewModels/BudgetDiscriminatorDto.cs` — single record `{ Id, Kind }`.
- `ProjectCeres/Controllers/Api/CategoryBudgetsApiController.cs` — 7 endpoints.
- `ProjectCeres/Controllers/Api/GoalBudgetsApiController.cs` — 7 endpoints.
- `ProjectCeres/Controllers/Api/BudgetsApiController.cs` — 1 discriminator endpoint.
- `ProjectCeres.Tests/Unit/Services/BudgetPeriodTests.cs` — covers all worked examples in spec §5.2 + leap-year edge cases.
- `ProjectCeres.Tests/Integration/Api/CategoryBudgetsCrudApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/CategoryBudgetsSpendApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/GoalBudgetsCrudApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/GoalBudgetsProgressApiTests.cs`
- `ProjectCeres.Tests/Integration/Api/BudgetsDiscriminatorApiTests.cs`

**Modified (server):**
- `ProjectCeres/Models/Settings.cs` — add `int BudgetPeriodStartDay { get; set; } = 1;`.
- `ProjectCeres/ViewModels/SettingsDtos.cs` — add `int BudgetPeriodStartDay` to `SettingsDto` and to `SettingsEditViewModel`.
- `ProjectCeres/Services/SettingsService.cs` — write the new field on update; default it on create.
- `ProjectCeres/Controllers/SettingsController.cs` (Razor) — accept the new field on the existing edit view (Razor settings page is still in use until the Settings SPA migration).
- `ProjectCeres/Views/Settings/Edit.cshtml` (Razor) — add input for the new field.
- `ProjectCeres/Controllers/Api/SettingsApiController.cs` — include `BudgetPeriodStartDay` in the GET response and add a `PATCH /api/settings/budget-period-start-day` endpoint.
- `ProjectCeres/Services/CategoryBudgetService.cs` — replace `GetActualSpendAsync` body to use `BudgetPeriod`; add `GetActualSpendForCurrentPeriodAsync(id)` overload that reads Settings and routes through. (Optional — may inline into the API list endpoint instead.)
- `ProjectCeres/Services/ICategoryBudgetService.cs` — keep signature; update XML docs.
- `ProjectCeres/Services/BudgetService.cs` — extend `GetAllAsync` with optional `string? currency` and `string? type` filters; add `ReactivateAsync(Guid id)` method.
- `ProjectCeres/Services/IBudgetService.cs` — interface updates.
- `ProjectCeres/Services/CategoryBudgetService.cs` — extend `GetAllAsync` with optional `string? currency`; add `ReactivateAsync(Guid id)` method that throws if reactivation would violate the unique active constraint.
- `ProjectCeres/Services/ICategoryBudgetService.cs` — interface updates.
- `ProjectCeres/Services/DashboardService.cs` (and/or `DashboardApiController.GetCategoryBudgets`) — call `BudgetPeriod.GetCurrentPeriodMonth(...)` to compute the right (year, month) before calling `GetActualSpendAsync`.
- `ProjectCeres/Controllers/BudgetsController.cs` — slim to 8 GET-only 302 redirects; delete all POST overloads; drop unused DI services.

**Deleted (server, in this plan):**
- `ProjectCeres/Views/Budgets/Index.cshtml`
- `ProjectCeres/Views/Budgets/Goals.cshtml`
- `ProjectCeres/Views/Budgets/Create.cshtml`
- `ProjectCeres/Views/Budgets/CreateGoal.cshtml`
- `ProjectCeres/Views/Budgets/Edit.cshtml`
- `ProjectCeres/Views/Budgets/EditGoal.cshtml`
- `ProjectCeres/Views/Budgets/Deactivate.cshtml`
- `ProjectCeres/Views/Budgets/DeactivateGoal.cshtml`

**Created (React client):**
- `ProjectCeres.Client/src/app/features/budgets/budgets-api.ts` — URL builders, DTOs.
- `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx` — page shell, tabs, +New dropdown, archive toggle.
- `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.tsx`
- `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.tsx`
- `ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.tsx` — Edit / Archive / Reactivate menu (works for both kinds).
- `ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.tsx` — Create + Edit form.
- `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.tsx` — Create + Edit form (Spending or Savings, based on `type` prop).
- `ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/BudgetCreate.tsx` — routed Create page; reads `?type=` and renders the right form.
- `ProjectCeres.Client/src/app/features/budgets/BudgetCreate.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/BudgetEdit.tsx` — routed Edit page; resolves discriminator then delegates.
- `ProjectCeres.Client/src/app/features/budgets/BudgetEdit.test.tsx`
- `ProjectCeres.Client/src/app/features/budgets/BudgetProgressBar.tsx` — shared progress-bar primitive used by both list pages and (later) by the dashboard.
- `ProjectCeres.Client/src/app/features/budgets/budget-display.ts` — labels for `Spending` / `Savings` / `Category Budget`, ordinals helper for the Settings toast (`1st`, `2nd`, ...).
- `ProjectCeres.Client/src/app/features/budgets/budget-display.test.ts`
- `ProjectCeres.Client/src/components/CurrencyCombobox.tsx` — new shared primitive for picking a currency by code (used by the budget forms).
- `ProjectCeres.Client/src/components/CurrencyCombobox.test.tsx`

**Modified (React client):**
- `ProjectCeres.Client/src/app/AppLayout.tsx` (or wherever routes are declared) — add the three Budgets routes.
- `ProjectCeres.Client/src/app/lib/use-settings.ts` — extend `AppSettings` type with `budgetPeriodStartDay: number`. Update `FALLBACK` to include `budgetPeriodStartDay: 1`.
- `ProjectCeres.Client/src/app/pages/Budgets.tsx` — replace placeholder with the new layout import.
- `ProjectCeres.Client/src/app/pages/Settings.tsx` — add the new "Budget period start day" field with stepper input.
- `ProjectCeres.Client/src/app/pages/Settings.test.tsx` — assert the new field renders + saves.
- `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` — render the conditional Budget picker on the Transaction variant. Loads active Spending Goals via the goal-budgets list endpoint.
- `ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx` — assert conditional rendering.
- `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx` — include `budgetId` in POST body.
- `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx` — include `budgetId` in PUT body; show archived goal in picker if currently tagged.

**Modified (docs):**
- `docs/models.md` — update `Settings.BudgetPeriodStartDay` row (1–31 range, drop Phase 3 annotation); annotate `CategoryBudget.PeriodStartDay` row as deferred.
- `docs/api-contract.md` — add 15 new endpoints.
- `docs/planning-phase3-spa-migration.md` — update Budgets controller row to Migrated; update §5 route map.
- `docs/planning-phase3.md` — mark step 5 (Budgets) as ✓.
- `CHANGELOG.md` — `[Unreleased]` entry.

---

## Sequencing rationale

This plan ships in seven phases. Each phase ends green (build clean, tests pass) so we can pause if needed.

1. **Tasks 1–3:** Server foundation. Settings column + DTO, BudgetPeriod helper + tests, dashboard wired through the helper. Nothing user-facing changes; existing tests stay green.
2. **Tasks 4–7:** Server API for Category Budgets. Service updates + 7 endpoints + integration tests.
3. **Tasks 8–11:** Server API for Goal Budgets + discriminator. Service updates + 7+1 endpoints + integration tests.
4. **Task 12:** Settings SPA — new field for the start day.
5. **Tasks 13–19:** SPA Budgets surface. Layout + tables + forms + Create/Edit pages.
6. **Task 20:** Movement form Spending-Goal picker.
7. **Tasks 21–22:** Razor demolition + doc sync.
8. **Task 23:** Final regression + manual smoke checklist.

---

## Task 1: Settings — `BudgetPeriodStartDay` column + DTO + Razor surface

**Files:**
- Modify: `ProjectCeres/Models/Settings.cs`
- Modify: `ProjectCeres/ViewModels/SettingsDtos.cs`
- Modify: `ProjectCeres/Services/SettingsService.cs`
- Modify: `ProjectCeres/Controllers/SettingsController.cs`
- Modify: `ProjectCeres/Views/Settings/Edit.cshtml`
- Modify: `ProjectCeres/Controllers/Api/SettingsApiController.cs`
- Create: `ProjectCeres/Migrations/<timestamp>_AddSettingsBudgetPeriodStartDay.cs`

The Settings entity, the SPA's `useSettings()` hook (Task 12), and the dashboard's period math all depend on this column existing. Ship it first so subsequent tasks can rely on it.

- [ ] **Step 1: Add the property to `Settings.cs`**

```csharp
namespace ProjectCeres.Models;

public class Settings
{
    public int Id { get; set; }
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public int DefaultCurrencyId { get; set; }
    /// <summary>
    /// Day of month (1–31) when budget periods start. Default 1 = calendar months.
    /// For months shorter than the configured day (e.g., 31 in April), the period
    /// starts on that month's last day. See BudgetPeriod helper.
    /// </summary>
    public int BudgetPeriodStartDay { get; set; } = 1;

    public Currency DefaultCurrency { get; set; } = null!;
}
```

- [ ] **Step 2: Extend `SettingsDtos.cs`**

```csharp
namespace ProjectCeres.ViewModels;

public record SettingsDto(
    string NumberFormat,
    string DateFormat,
    string DefaultCurrencyCode,
    string DefaultCurrencySymbol,
    int    BudgetPeriodStartDay);

public class SettingsEditViewModel
{
    public string NumberFormat { get; set; } = string.Empty;
    public string DateFormat { get; set; } = string.Empty;
    public int? DefaultCurrencyId { get; set; }
    public int BudgetPeriodStartDay { get; set; } = 1;
}
```

(If the existing `SettingsEditViewModel` is in a different file, edit it where it lives. Use `grep -rn "SettingsEditViewModel" ProjectCeres/` to locate.)

- [ ] **Step 3: Update `SettingsService.cs`**

Replace `UpdateAsync` and `CreateDefaults`:

```csharp
public async Task UpdateAsync(SettingsEditViewModel vm)
{
    var existing = await db.Settings.FirstOrDefaultAsync();
    var isNew    = existing is null;
    var settings = existing ?? CreateDefaults();

    settings.NumberFormat         = vm.NumberFormat;
    settings.DateFormat           = vm.DateFormat;
    settings.DefaultCurrencyId    = vm.DefaultCurrencyId!.Value;
    settings.BudgetPeriodStartDay = ClampStartDay(vm.BudgetPeriodStartDay);

    if (isNew)
        db.Settings.Add(settings);

    await db.SaveChangesAsync();
}

private static int ClampStartDay(int value) => value < 1 ? 1 : value > 31 ? 31 : value;

private static Settings CreateDefaults() => new()
{
    Id                   = 1,
    NumberFormat         = "comma_decimal",
    DateFormat           = "DD/MM/YYYY",
    DefaultCurrencyId    = 1,
    BudgetPeriodStartDay = 1
};
```

- [ ] **Step 4: Update `SettingsApiController.cs` GET response**

```csharp
[HttpGet]
public async Task<IActionResult> Get()
{
    var settings = await settingsService.GetAsync();
    var dto = new SettingsDto(
        NumberFormat:           settings.NumberFormat,
        DateFormat:             settings.DateFormat,
        DefaultCurrencyCode:    settings.DefaultCurrency.Code,
        DefaultCurrencySymbol:  settings.DefaultCurrency.Symbol,
        BudgetPeriodStartDay:   settings.BudgetPeriodStartDay);
    return Ok(dto);
}
```

- [ ] **Step 5: Update Razor `SettingsController.cs`** — read existing `Edit(GET)` and `Edit(POST)`. Add the new field to the view-model binding so the Razor surface keeps working until that page is also migrated. Open the file, find where `SettingsEditViewModel` is populated from the loaded settings, add `BudgetPeriodStartDay = settings.BudgetPeriodStartDay`. The POST already passes `vm` straight to `UpdateAsync` — no change there.

- [ ] **Step 6: Update `Views/Settings/Edit.cshtml`**

Add a new field block matching the existing pattern. Read the file to copy the field-block style; add:

```cshtml
<div class="form-group mt-3">
    <label asp-for="BudgetPeriodStartDay" class="form-label">Budget period start day</label>
    <input asp-for="BudgetPeriodStartDay" type="number" min="1" max="31" class="form-control" />
    <small class="form-text text-muted">
        All your budget cycles start on this day. Use 1 for calendar months. If a month
        doesn't have this day (e.g., 31 in April), the cycle starts on that month's last day.
    </small>
    <span asp-validation-for="BudgetPeriodStartDay" class="text-danger"></span>
</div>
```

- [ ] **Step 7: Generate the EF migration**

```bash
dotnet ef migrations add AddSettingsBudgetPeriodStartDay --project ProjectCeres
```

Verify the generated migration only adds the one column with `defaultValue: 1`. Open the generated `<timestamp>_AddSettingsBudgetPeriodStartDay.cs` and confirm `Up` looks like:

```csharp
migrationBuilder.AddColumn<int>(
    name: "BudgetPeriodStartDay",
    table: "Settings",
    type: "integer",
    nullable: false,
    defaultValue: 1);
```

- [ ] **Step 8: Apply the migration**

```bash
dotnet ef database update --project ProjectCeres
```

Expected: success, one column added to the Settings table.

- [ ] **Step 9: Build + run server tests**

```bash
dotnet build
dotnet test ProjectCeres.Tests
```

Expected: build succeeds with 0 warnings/errors; all 417 existing server tests pass (the new column is additive).

- [ ] **Step 10: Commit**

```bash
git add ProjectCeres/Models/Settings.cs \
        ProjectCeres/ViewModels/SettingsDtos.cs \
        ProjectCeres/Services/SettingsService.cs \
        ProjectCeres/Controllers/SettingsController.cs \
        ProjectCeres/Views/Settings/Edit.cshtml \
        ProjectCeres/Controllers/Api/SettingsApiController.cs \
        ProjectCeres/Migrations/
git commit -m "feat(settings): add BudgetPeriodStartDay column (1–31, default 1)"
```

---

## Task 2: `BudgetPeriod` static helper + unit tests

**Files:**
- Create: `ProjectCeres/Services/BudgetPeriod.cs`
- Create: `ProjectCeres.Tests/Unit/Services/BudgetPeriodTests.cs`

The pure period-boundary math, exhaustively tested. Spec §5.2 + §5.3 are the contract.

- [ ] **Step 1: Write failing tests**

```csharp
using FluentAssertions;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit.Services;

public class BudgetPeriodTests
{
    [Theory]
    // (year, month, startDay, expectedStart, expectedEnd)
    [InlineData(2026, 5,  1, "2026-05-01", "2026-05-31")] // Default — calendar month
    [InlineData(2026, 5, 25, "2026-04-25", "2026-05-24")] // Mid-month start, mid-period
    [InlineData(2026, 4, 31, "2026-03-31", "2026-04-29")] // Start=31, April fallback to 30 → end=29
    [InlineData(2026, 5, 31, "2026-04-30", "2026-05-30")] // Start=31, May has 31 → end=30
    [InlineData(2026, 3, 31, "2026-02-28", "2026-03-30")] // Feb fallback (non-leap) → start=Feb 28
    [InlineData(2024, 3, 31, "2024-02-29", "2024-03-30")] // Feb fallback (leap) → start=Feb 29
    [InlineData(2026, 2, 30, "2026-01-30", "2026-02-27")] // Start=30, Feb fallback → end=Feb 27 (Feb 28 - 1)
    [InlineData(2024, 2, 30, "2024-01-30", "2024-02-28")] // Start=30, leap Feb → end=Feb 28 (Feb 29 - 1)
    [InlineData(2026, 6,  1, "2026-06-01", "2026-06-30")] // June calendar
    [InlineData(2026, 1,  1, "2026-01-01", "2026-01-31")] // January calendar
    public void GetBoundsForMonth_returns_expected_range(
        int year, int month, int startDay, string expectedStart, string expectedEnd)
    {
        var (start, end) = BudgetPeriod.GetBoundsForMonth(year, month, startDay);

        start.Should().Be(DateOnly.Parse(expectedStart));
        end.Should().Be(DateOnly.Parse(expectedEnd));
    }

    [Theory]
    // (today, startDay, expectedYear, expectedMonth)
    [InlineData("2026-05-01",  1, 2026, 5)]
    [InlineData("2026-05-01", 25, 2026, 5)] // 2026-05-01 falls in Apr 25 – May 24 → period name May
    [InlineData("2026-04-24", 25, 2026, 4)] // 2026-04-24 falls in Mar 25 – Apr 24 → period name April
    [InlineData("2026-04-25", 25, 2026, 5)] // First day of new period → name flips to May
    [InlineData("2026-04-10", 31, 2026, 4)] // Today between Mar 31 (start) and Apr 29 (end) → April
    [InlineData("2026-05-10", 31, 2026, 5)] // Today between Apr 30 (start) and May 30 (end) → May
    [InlineData("2026-12-31",  1, 2026, 12)]
    public void GetCurrentPeriodMonth_returns_expected_period_name(
        string today, int startDay, int expectedYear, int expectedMonth)
    {
        var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(DateOnly.Parse(today), startDay);

        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-5)]
    public void GetBoundsForMonth_rejects_invalid_start_day(int startDay)
    {
        Action act = () => BudgetPeriod.GetBoundsForMonth(2026, 5, startDay);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run — expect FAIL (helper doesn't exist yet)**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~BudgetPeriodTests"
```

- [ ] **Step 3: Implement `BudgetPeriod.cs`**

```csharp
namespace ProjectCeres.Services;

/// <summary>
/// Pure helper for budget-period boundary math driven by
/// <c>Settings.BudgetPeriodStartDay</c>. See spec §5.2 / §5.3 for the rule:
/// for shorter months, the start day falls back to the month's last day;
/// the period whose end falls in (year, month) is named after that month.
/// </summary>
public static class BudgetPeriod
{
    /// <summary>
    /// Given a target period-name (year, month) and the configured start day (1–31),
    /// returns the [start, end] DateOnly range of that period.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetBoundsForMonth(int year, int month, int startDay)
    {
        if (startDay < 1 || startDay > 31)
            throw new ArgumentOutOfRangeException(nameof(startDay), "Start day must be 1–31.");

        // The period whose END falls in (year, month) starts in the previous calendar month
        // (or in (year, month) itself when startDay = 1, which is a degenerate case).
        // End = (next period's start) - 1 day.
        // Start = the start day of (year, month)'s previous period.

        // Step 1: the start day of (year, month) — the day the NEXT period begins.
        var nextStart = ApplyStartDayInMonth(year, month, startDay);

        // Step 2: walk back one month to get this period's start.
        var prev = nextStart.AddMonths(-1);
        var thisStart = ApplyStartDayInMonth(prev.Year, prev.Month, startDay);

        var end = nextStart.AddDays(-1);
        return (thisStart, end);
    }

    /// <summary>
    /// Given today and the configured start day, returns (year, month) of the
    /// CURRENT period — the period whose end falls in that calendar month.
    /// </summary>
    public static (int Year, int Month) GetCurrentPeriodMonth(DateOnly today, int startDay)
    {
        if (startDay < 1 || startDay > 31)
            throw new ArgumentOutOfRangeException(nameof(startDay), "Start day must be 1–31.");

        // The "current period" is the one whose end is on or after today AND
        // whose start is on or before today. We find it by checking: is today
        // before this calendar month's start day? If yes, the current period
        // ends in this month. If no, it ends in the next month.

        var thisMonthStart = ApplyStartDayInMonth(today.Year, today.Month, startDay);

        if (today < thisMonthStart)
        {
            // Today is before this month's start day → period ends in this calendar month.
            return (today.Year, today.Month);
        }

        // Today is on or after this month's start day → period ends in the NEXT calendar month.
        var next = today.AddMonths(1);
        return (next.Year, next.Month);
    }

    /// <summary>
    /// Returns DateOnly(year, month, min(startDay, lastDayOfMonth)).
    /// </summary>
    private static DateOnly ApplyStartDayInMonth(int year, int month, int startDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var actualDay   = Math.Min(startDay, daysInMonth);
        return new DateOnly(year, month, actualDay);
    }
}
```

- [ ] **Step 4: Run — expect PASS (all 19 cases)**

```bash
dotnet test ProjectCeres.Tests --filter "FullyQualifiedName~BudgetPeriodTests"
```

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/BudgetPeriod.cs \
        ProjectCeres.Tests/Unit/Services/BudgetPeriodTests.cs
git commit -m "feat(budgets): BudgetPeriod helper for period-boundary math"
```

---

## Task 3: Wire `BudgetPeriod` into `CategoryBudgetService` + dashboard

**Files:**
- Modify: `ProjectCeres/Services/CategoryBudgetService.cs`
- Modify: `ProjectCeres/Controllers/Api/DashboardApiController.cs`

`GetActualSpendAsync(id, year, month)` keeps its signature; semantics shift.

- [ ] **Step 1: Replace the body of `GetActualSpendAsync`** in `CategoryBudgetService.cs`:

```csharp
public async Task<decimal> GetActualSpendAsync(Guid id, int year, int month)
{
    var budget = await db.CategoryBudgets.FindAsync(id)
        ?? throw new InvalidOperationException($"CategoryBudget {id} not found.");

    var settings = await db.Settings.FirstOrDefaultAsync()
        ?? throw new InvalidOperationException("Settings row missing.");

    var (periodStart, periodEnd) =
        BudgetPeriod.GetBoundsForMonth(year, month, settings.BudgetPeriodStartDay);

    return await db.Transactions
        .Where(t =>
            t.CategoryId == budget.CategoryId &&
            t.Account.CurrencyId == budget.CurrencyId &&
            t.Date >= periodStart &&
            t.Date <= periodEnd)
        .SumAsync(t => (decimal?)t.Amount) ?? 0m;
}
```

- [ ] **Step 2: Update `DashboardApiController.GetCategoryBudgets`** to compute the period-name month before calling the service:

```csharp
[HttpGet("category-budgets")]
public async Task<IActionResult> GetCategoryBudgets()
{
    var budgets  = await categoryBudgetService.GetAllAsync(includeInactive: false);
    var settings = await settingsService.GetAsync();
    var today    = DateOnly.FromDateTime(DateTime.Today);
    var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(today, settings.BudgetPeriodStartDay);

    var result = new List<object>();
    foreach (var budget in budgets)
    {
        var spent = await categoryBudgetService.GetActualSpendAsync(budget.Id, year, month);
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
```

- [ ] **Step 3: Build + run all server tests**

```bash
dotnet build
dotnet test ProjectCeres.Tests
```

Expected: build clean, all 417+19 tests pass. **If any existing CategoryBudget test fails because it seeded transactions assuming calendar-month semantics, fix the test fixture to either set `BudgetPeriodStartDay = 1` explicitly OR update the assertion.** (`BudgetPeriodStartDay = 1` is the seeded default, so most should pass unchanged.)

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Services/CategoryBudgetService.cs \
        ProjectCeres/Controllers/Api/DashboardApiController.cs
git commit -m "feat(budgets): CategoryBudget actual-spend respects BudgetPeriodStartDay"
```

---

## Task 4: Server — `CategoryBudgetService` extensions

**Files:**
- Modify: `ProjectCeres/Services/ICategoryBudgetService.cs`
- Modify: `ProjectCeres/Services/CategoryBudgetService.cs`

Adds:
- `currency` filter on `GetAllAsync`.
- `ReactivateAsync(Guid id)` that throws if reactivation would violate the unique active constraint.
- A 409-shaped exception class so the API can map exception → 409 cleanly.

- [ ] **Step 1: Define the conflict exception**

Create `ProjectCeres/Services/DuplicateBudgetException.cs`:

```csharp
namespace ProjectCeres.Services;

/// <summary>
/// Thrown when a CategoryBudget Create or Reactivate would violate the
/// unique-active-per-(Category, Currency) constraint. Carries the existing
/// budget's id and active state so the API layer can return a 409 with
/// enough info for the SPA to offer a one-click reactivate.
/// </summary>
public sealed class DuplicateBudgetException(Guid existingBudgetId, bool existingIsActive)
    : Exception("A budget for this category and currency already exists.")
{
    public Guid ExistingBudgetId { get; } = existingBudgetId;
    public bool ExistingIsActive { get; } = existingIsActive;
}
```

- [ ] **Step 2: Update `ICategoryBudgetService.cs`**

```csharp
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICategoryBudgetService
{
    Task<IEnumerable<CategoryBudget>> GetAllAsync(bool includeInactive = false, string? currency = null);
    Task<CategoryBudget?> GetByIdAsync(Guid id);
    Task<CategoryBudget> CreateAsync(CategoryBudgetCreateViewModel vm);
    Task UpdateAsync(CategoryBudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    Task ReactivateAsync(Guid id);
    Task<decimal> GetActualSpendAsync(Guid id, int year, int month);
}
```

- [ ] **Step 3: Update `CategoryBudgetService.cs`** — extend `GetAllAsync`:

```csharp
public async Task<IEnumerable<CategoryBudget>> GetAllAsync(bool includeInactive = false, string? currency = null)
{
    var query = db.CategoryBudgets
        .Include(cb => cb.Category).ThenInclude(c => c.CategoryType)
        .Include(cb => cb.Currency)
        .AsQueryable();

    if (!includeInactive)
        query = query.Where(cb => cb.IsActive);

    if (!string.IsNullOrWhiteSpace(currency))
        query = query.Where(cb => cb.Currency.Code == currency);

    return await query.OrderBy(cb => cb.Category.Name).ToListAsync();
}
```

- [ ] **Step 4: Update `CreateAsync` to throw `DuplicateBudgetException`**:

```csharp
public async Task<CategoryBudget> CreateAsync(CategoryBudgetCreateViewModel vm)
{
    var category = await db.Categories
        .Include(c => c.CategoryType)
        .FirstOrDefaultAsync(c => c.Id == vm.CategoryId)
        ?? throw new InvalidOperationException("Category not found.");

    if (category.CategoryType.Name != "Expense")
        throw new InvalidOperationException(
            "CategoryBudget can only be applied to Expense categories.");

    var existing = await db.CategoryBudgets.FirstOrDefaultAsync(cb =>
        cb.CategoryId == vm.CategoryId &&
        cb.CurrencyId == vm.CurrencyId);

    if (existing is not null)
        throw new DuplicateBudgetException(existing.Id, existing.IsActive);

    var budget = new CategoryBudget
    {
        Id          = Guid.NewGuid(),
        CategoryId  = vm.CategoryId!.Value,
        CurrencyId  = vm.CurrencyId!.Value,
        LimitAmount = vm.LimitAmount,
        IsActive    = true
    };

    db.CategoryBudgets.Add(budget);
    await db.SaveChangesAsync();
    return budget;
}
```

- [ ] **Step 5: Add `ReactivateAsync`**:

```csharp
public async Task ReactivateAsync(Guid id)
{
    var budget = await db.CategoryBudgets.FindAsync(id)
        ?? throw new InvalidOperationException($"CategoryBudget {id} not found.");

    if (budget.IsActive) return;

    var conflict = await db.CategoryBudgets.FirstOrDefaultAsync(cb =>
        cb.CategoryId == budget.CategoryId &&
        cb.CurrencyId == budget.CurrencyId &&
        cb.IsActive);

    if (conflict is not null)
        throw new DuplicateBudgetException(conflict.Id, true);

    budget.IsActive = true;
    await db.SaveChangesAsync();
}
```

- [ ] **Step 6: Build + tests**

```bash
dotnet build && dotnet test ProjectCeres.Tests
```

Expected: clean. (Existing tests don't exercise the new branches yet; they'll be added in Task 5.)

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/ICategoryBudgetService.cs \
        ProjectCeres/Services/CategoryBudgetService.cs \
        ProjectCeres/Services/DuplicateBudgetException.cs
git commit -m "feat(budgets): CategoryBudgetService gains currency filter, reactivate, conflict exception"
```

---

## Task 5: Server — `CategoryBudgetsApiController` + DTOs + integration tests

**Files:**
- Create: `ProjectCeres/ViewModels/CategoryBudgetCrudDtos.cs`
- Create: `ProjectCeres/Controllers/Api/CategoryBudgetsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/CategoryBudgetsCrudApiTests.cs`
- Create: `ProjectCeres.Tests/Integration/Api/CategoryBudgetsSpendApiTests.cs`

7 endpoints. Tested end-to-end via the existing integration test factory.

- [ ] **Step 1: Define DTOs** in `CategoryBudgetCrudDtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record CategoryBudgetListItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CurrencyCode,
    string CurrencySymbol,
    decimal LimitAmount,
    bool IsActive,
    decimal CurrentPeriodSpend,
    DateOnly CurrentPeriodEnd);

public record CategoryBudgetEditDto(
    Guid Id,
    Guid CategoryId,
    int CurrencyId,
    string CurrencyCode,
    decimal LimitAmount,
    bool IsActive);

public class CreateCategoryBudgetRequest
{
    [Required] public Guid? CategoryId { get; set; }
    [Required] public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal LimitAmount { get; set; }
}

public class UpdateCategoryBudgetRequest
{
    [Required] public Guid? CategoryId { get; set; }
    [Required] public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal LimitAmount { get; set; }
    public bool IsActive { get; set; } = true;
}

public record CategoryBudgetSpendDto(
    decimal Spent,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);
```

- [ ] **Step 2: Implement `CategoryBudgetsApiController.cs`**:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/category-budgets")]
public class CategoryBudgetsApiController(
    ICategoryBudgetService categoryBudgetService,
    ISettingsService settingsService,
    AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryBudgetListItemDto>>> Get(
        [FromQuery] string? currency = null,
        [FromQuery] bool includeArchived = false)
    {
        var budgets  = await categoryBudgetService.GetAllAsync(includeInactive: includeArchived, currency: currency);
        var settings = await settingsService.GetAsync();
        var today    = DateOnly.FromDateTime(DateTime.Today);
        var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(today, settings.BudgetPeriodStartDay);
        var (_, periodEnd) = BudgetPeriod.GetBoundsForMonth(year, month, settings.BudgetPeriodStartDay);

        var dtos = new List<CategoryBudgetListItemDto>();
        foreach (var b in budgets)
        {
            var spent = await categoryBudgetService.GetActualSpendAsync(b.Id, year, month);
            dtos.Add(new CategoryBudgetListItemDto(
                Id:                 b.Id,
                CategoryId:         b.CategoryId,
                CategoryName:       b.Category.Name,
                CurrencyCode:       b.Currency.Code,
                CurrencySymbol:     b.Currency.Symbol,
                LimitAmount:        b.LimitAmount,
                IsActive:           b.IsActive,
                CurrentPeriodSpend: spent,
                CurrentPeriodEnd:   periodEnd));
        }
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryBudgetEditDto>> GetById(Guid id)
    {
        var b = await categoryBudgetService.GetByIdAsync(id);
        if (b is null) return NotFound();
        return new CategoryBudgetEditDto(
            Id:             b.Id,
            CategoryId:     b.CategoryId,
            CurrencyId:     b.CurrencyId,
            CurrencyCode:   b.Currency.Code,
            LimitAmount:    b.LimitAmount,
            IsActive:       b.IsActive);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCategoryBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var vm = new CategoryBudgetCreateViewModel
        {
            CategoryId  = request.CategoryId,
            CurrencyId  = request.CurrencyId,
            LimitAmount = request.LimitAmount
        };

        try
        {
            var budget = await categoryBudgetService.CreateAsync(vm);
            return Created($"/api/category-budgets/{budget.Id}", new { id = budget.Id });
        }
        catch (DuplicateBudgetException ex)
        {
            return Conflict(new
            {
                error = new
                {
                    code             = "DUPLICATE_BUDGET",
                    message          = ex.Message,
                    existingBudgetId = ex.ExistingBudgetId,
                    existingIsActive = ex.ExistingIsActive
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCategoryBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var vm = new CategoryBudgetEditViewModel
        {
            Id          = id,
            CategoryId  = request.CategoryId,
            CurrencyId  = request.CurrencyId,
            LimitAmount = request.LimitAmount
        };

        try
        {
            await categoryBudgetService.UpdateAsync(vm);
            // IsActive changes are not handled by Update; use archive/reactivate endpoints instead.
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await categoryBudgetService.DeactivateAsync(id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        try
        {
            await categoryBudgetService.ReactivateAsync(id);
            return NoContent();
        }
        catch (DuplicateBudgetException ex)
        {
            return Conflict(new
            {
                error = new
                {
                    code             = "DUPLICATE_BUDGET",
                    message          = ex.Message,
                    existingBudgetId = ex.ExistingBudgetId,
                    existingIsActive = ex.ExistingIsActive
                }
            });
        }
    }

    [HttpGet("{id:guid}/spend")]
    public async Task<ActionResult<CategoryBudgetSpendDto>> GetSpend(Guid id, [FromQuery] int year, [FromQuery] int month)
    {
        var existing = await categoryBudgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var settings = await settingsService.GetAsync();
        var (start, end) = BudgetPeriod.GetBoundsForMonth(year, month, settings.BudgetPeriodStartDay);
        var spent = await categoryBudgetService.GetActualSpendAsync(id, year, month);

        return new CategoryBudgetSpendDto(spent, start, end);
    }

    private object ToValidationEnvelope() => new
    {
        error = new
        {
            code    = "VALIDATION_ERROR",
            message = "Validation failed.",
            details = ModelState
                .Where(kv => kv.Value!.Errors.Count > 0)
                .SelectMany(kv => kv.Value!.Errors.Select(e => new { field = kv.Key, message = e.ErrorMessage }))
                .ToArray()
        }
    };
}
```

- [ ] **Step 3: Write integration tests — `CategoryBudgetsCrudApiTests.cs`**:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class CategoryBudgetsCrudApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededBudgetIds = [];

    public CategoryBudgetsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededBudgetIds.Count > 0)
            await db.CategoryBudgets.Where(cb => _seededBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetList_returns_active_budgets_with_currency_filter()
    {
        var response = await _client.GetAsync("/api/category-budgets?currency=EUR");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("currentPeriodSpend"); // shape sanity
    }

    [Fact]
    public async Task Post_then_Get_works_end_to_end()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 750m
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var read = await _client.GetAsync($"/api/category-budgets/{id}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await read.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("limitAmount").GetDecimal().Should().Be(750m);
        dto.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Post_returns_409_on_duplicate_active_budget()
    {
        // First budget for Housing+EUR (CurrencyId=1).
        var first = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 500m
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(firstId);

        // Second budget for the same combination → 409.
        var second = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 800m
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("DUPLICATE_BUDGET");
        body.GetProperty("error").GetProperty("existingBudgetId").GetGuid().Should().Be(firstId);
        body.GetProperty("error").GetProperty("existingIsActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Put_updates_limit_amount()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var update = await _client.PutAsJsonAsync($"/api/category-budgets/{id}", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 900m,
            isActive    = true
        });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.LimitAmount.Should().Be(900m);
    }

    [Fact]
    public async Task Archive_sets_isActive_false()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var archive = await _client.PatchAsync($"/api/category-budgets/{id}/archive", null);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivate_after_archive_sets_isActive_true()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        await _client.PatchAsync($"/api/category-budgets/{id}/archive", null);
        var react = await _client.PatchAsync($"/api/category-budgets/{id}/reactivate", null);
        react.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_returns_409_when_active_dup_exists()
    {
        // First active.
        var first = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 500m
        });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(firstId);

        // Archive it.
        await _client.PatchAsync($"/api/category-budgets/{firstId}/archive", null);

        // Second active for the same combination.
        var second = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 700m
        });
        second.StatusCode.Should().Be(HttpStatusCode.Created); // First was archived, so no conflict.
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(secondId);

        // Now try to reactivate the first → 409.
        var react = await _client.PatchAsync($"/api/category-budgets/{firstId}/reactivate", null);
        react.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
```

(Note: the second-budget test passes 201 because the duplicate check is `IsActive`-aware after the rewrite in Task 4 step 4 — wait, actually our rewrite checks for ANY existing row, not just active ones. Read Task 4 step 4 again — it throws `DuplicateBudgetException` whenever a (Category, Currency) row exists, active or archived. That means the test above would produce a 409 on the second create, NOT a 201. **Adjust the test** to:
1. Create the first budget.
2. Archive it.
3. Try to create another one with the same (Category, Currency) — expect 409 with `existingIsActive=false`.
4. Hit reactivate on the first id — should succeed (201 → 204) and confirm IsActive=true.
5. Skip the "second active" branch entirely.)

Refine the last two tests accordingly when writing them. The simpler shape:

```csharp
[Fact]
public async Task Post_returns_409_with_existingIsActive_false_when_archived_dup_exists()
{
    var first = await _client.PostAsJsonAsync("/api/category-budgets", new
    {
        categoryId  = HousingCategoryId,
        currencyId  = 1,
        limitAmount = 500m
    });
    var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    _seededBudgetIds.Add(firstId);

    await _client.PatchAsync($"/api/category-budgets/{firstId}/archive", null);

    var second = await _client.PostAsJsonAsync("/api/category-budgets", new
    {
        categoryId  = HousingCategoryId,
        currencyId  = 1,
        limitAmount = 700m
    });
    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await second.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("error").GetProperty("existingIsActive").GetBoolean().Should().BeFalse();
}
```

- [ ] **Step 4: Write `CategoryBudgetsSpendApiTests.cs`** — verifies the spend endpoint respects `BudgetPeriodStartDay`. Seed Settings with start day = 25, seed two transactions on different sides of the boundary, assert the spend response includes only transactions inside the period:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class CategoryBudgetsSpendApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededBudgetIds = [];
    private int? _originalStartDay;

    public CategoryBudgetsSpendApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.FirstAsync();
        _originalStartDay = settings.BudgetPeriodStartDay;
        settings.BudgetPeriodStartDay = 25;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededBudgetIds.Count > 0)
            await db.CategoryBudgets.Where(cb => _seededBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_originalStartDay.HasValue)
        {
            var settings = await db.Settings.FirstAsync();
            settings.BudgetPeriodStartDay = _originalStartDay.Value;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetSpend_with_startDay25_includes_only_in_period_transactions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account { Id = Guid.NewGuid(), Name = $"BS-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);

        var budget = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 500m,
            IsActive    = true
        };
        db.CategoryBudgets.Add(budget);
        _seededBudgetIds.Add(budget.Id);

        // Transactions: in-period (Apr 25 – May 24) and out-of-period.
        var inPeriod = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId,
                                         Date = new DateOnly(2026, 5, 1), Amount = 100m };
        var outOfPeriod = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId,
                                            Date = new DateOnly(2026, 4, 24), Amount = 999m };
        db.Transactions.AddRange(inPeriod, outOfPeriod);
        _seededTransactionIds.AddRange([inPeriod.Id, outOfPeriod.Id]);
        await db.SaveChangesAsync();

        // year=2026, month=5 → period is Apr 25 – May 24 (per BudgetPeriod with start=25)
        var response = await _client.GetAsync($"/api/category-budgets/{budget.Id}/spend?year=2026&month=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("spent").GetDecimal().Should().Be(100m);
        dto.GetProperty("periodStart").GetString().Should().Be("2026-04-25");
        dto.GetProperty("periodEnd").GetString().Should().Be("2026-05-24");
    }
}
```

- [ ] **Step 5: Run — expect tests for new endpoints to PASS, existing tests to stay green**

```bash
dotnet test ProjectCeres.Tests
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/ViewModels/CategoryBudgetCrudDtos.cs \
        ProjectCeres/Controllers/Api/CategoryBudgetsApiController.cs \
        ProjectCeres.Tests/Integration/Api/CategoryBudgetsCrudApiTests.cs \
        ProjectCeres.Tests/Integration/Api/CategoryBudgetsSpendApiTests.cs
git commit -m "feat(api): CategoryBudgetsApiController — list, CRUD, archive/reactivate, spend"
```

---

## Task 6: Server — `BudgetService` extensions

**Files:**
- Modify: `ProjectCeres/Services/IBudgetService.cs`
- Modify: `ProjectCeres/Services/BudgetService.cs`

Adds `currency` + `type` filters to `GetAllAsync`, plus `ReactivateAsync`. (No DuplicateBudgetException for Goal Budgets — they have no business-key uniqueness constraint.)

- [ ] **Step 1: Update `IBudgetService.cs`**

```csharp
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IBudgetService
{
    Task<IEnumerable<Budget>> GetAllAsync(
        bool includeInactive = false,
        string? currency = null,
        string? type = null);

    Task<Budget?> GetByIdAsync(Guid id);
    Task<Budget> CreateAsync(BudgetCreateViewModel vm);
    Task UpdateAsync(BudgetEditViewModel vm);
    Task DeactivateAsync(Guid id);
    Task ReactivateAsync(Guid id);
    Task<decimal> GetActualSpendAsync(Guid id);
    Task<BudgetProgressResult> GetProgressAsync(Guid id);
}
```

- [ ] **Step 2: Update `BudgetService.cs` — `GetAllAsync`**:

```csharp
public async Task<IEnumerable<Budget>> GetAllAsync(
    bool includeInactive = false,
    string? currency = null,
    string? type = null)
{
    var query = db.Budgets
        .Include(b => b.Currency)
        .Include(b => b.LinkedAccount)
        .AsQueryable();

    if (!includeInactive)
        query = query.Where(b => b.IsActive);

    if (!string.IsNullOrWhiteSpace(currency))
        query = query.Where(b => b.Currency.Code == currency);

    if (!string.IsNullOrWhiteSpace(type))
    {
        var goalType = type.ToLowerInvariant() switch
        {
            "spending" => "Spending",
            "savings"  => "Savings",
            _          => null
        };
        if (goalType is null)
            throw new ArgumentException("type must be 'spending' or 'savings'.", nameof(type));
        query = query.Where(b => b.GoalType == goalType);
    }

    return await query
        .OrderByDescending(b => b.IsActive)
        .ThenBy(b => b.EndDate ?? DateOnly.MaxValue)
        .ToListAsync();
}
```

- [ ] **Step 3: Update `BudgetService.cs` — `CreateAsync`** to validate `EndDate >= StartDate`:

```csharp
public async Task<Budget> CreateAsync(BudgetCreateViewModel vm)
{
    ValidateGoalTypeRules(vm.GoalType, vm.LinkedAccountId);
    ValidateDateRange(vm.StartDate, vm.EndDate);

    // Savings goals: derive currency from linked account if not provided.
    int currencyId = vm.CurrencyId ?? 0;
    if (vm.GoalType == "Savings" && vm.LinkedAccountId.HasValue)
    {
        var account = await db.Accounts.FindAsync(vm.LinkedAccountId.Value)
            ?? throw new InvalidOperationException("Linked account not found.");
        currencyId = account.CurrencyId;
    }
    if (currencyId == 0)
        throw new InvalidOperationException("CurrencyId is required for Spending goals.");

    var budget = new Budget
    {
        Id              = Guid.NewGuid(),
        Name            = vm.Name,
        TargetAmount    = vm.TargetAmount,
        CurrencyId      = currencyId,
        StartDate       = vm.StartDate,
        EndDate         = vm.EndDate,
        Description     = vm.Description,
        GoalType        = vm.GoalType,
        LinkedAccountId = vm.LinkedAccountId,
        IsActive        = true
    };

    db.Budgets.Add(budget);
    await db.SaveChangesAsync();
    return budget;
}

private static void ValidateDateRange(DateOnly startDate, DateOnly? endDate)
{
    if (endDate.HasValue && endDate.Value < startDate)
        throw new InvalidOperationException("End date must be on or after start date.");
}
```

- [ ] **Step 4: Update `UpdateAsync`** with the same date-range validation:

```csharp
public async Task UpdateAsync(BudgetEditViewModel vm)
{
    var budget = await db.Budgets.FindAsync(vm.Id)
        ?? throw new InvalidOperationException($"Budget {vm.Id} not found.");

    ValidateGoalTypeRules(vm.GoalType, vm.LinkedAccountId);
    ValidateDateRange(vm.StartDate, vm.EndDate);

    int currencyId = vm.CurrencyId ?? budget.CurrencyId;
    if (vm.GoalType == "Savings" && vm.LinkedAccountId.HasValue)
    {
        var account = await db.Accounts.FindAsync(vm.LinkedAccountId.Value)
            ?? throw new InvalidOperationException("Linked account not found.");
        currencyId = account.CurrencyId;
    }

    budget.Name            = vm.Name;
    budget.TargetAmount    = vm.TargetAmount;
    budget.CurrencyId      = currencyId;
    budget.StartDate       = vm.StartDate;
    budget.EndDate         = vm.EndDate;
    budget.Description     = vm.Description;
    budget.GoalType        = vm.GoalType;
    budget.LinkedAccountId = vm.LinkedAccountId;
    await db.SaveChangesAsync();
}
```

- [ ] **Step 5: Add `ReactivateAsync`**:

```csharp
public async Task ReactivateAsync(Guid id)
{
    var budget = await db.Budgets.FindAsync(id)
        ?? throw new InvalidOperationException($"Budget {id} not found.");

    if (budget.IsActive) return;
    budget.IsActive = true;
    await db.SaveChangesAsync();
}
```

- [ ] **Step 6: Build + tests**

```bash
dotnet build && dotnet test ProjectCeres.Tests
```

Expected: clean.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Services/IBudgetService.cs \
        ProjectCeres/Services/BudgetService.cs
git commit -m "feat(budgets): BudgetService gains currency/type filters, reactivate, date-range validation"
```

---

## Task 7: Server — `GoalBudgetsApiController` + DTOs + integration tests

**Files:**
- Create: `ProjectCeres/ViewModels/GoalBudgetCrudDtos.cs`
- Create: `ProjectCeres/Controllers/Api/GoalBudgetsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/GoalBudgetsCrudApiTests.cs`
- Create: `ProjectCeres.Tests/Integration/Api/GoalBudgetsProgressApiTests.cs`

7 endpoints. Same pattern as Category Budgets but no 409 logic.

- [ ] **Step 1: Define DTOs** in `GoalBudgetCrudDtos.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record GoalBudgetListItemDto(
    Guid Id,
    string Name,
    string GoalType,
    string CurrencyCode,
    string CurrencySymbol,
    decimal TargetAmount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description,
    bool IsActive,
    Guid? LinkedAccountId,
    string? LinkedAccountName,
    decimal Progress);

public record GoalBudgetEditDto(
    Guid Id,
    string Name,
    string GoalType,
    int CurrencyId,
    string CurrencyCode,
    decimal TargetAmount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description,
    bool IsActive,
    Guid? LinkedAccountId);

public class CreateGoalBudgetRequest
{
    [Required, MinLength(1)] public string Name { get; set; } = string.Empty;
    [Required] public string GoalType { get; set; } = "Spending";
    public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal TargetAmount { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Description { get; set; }
    public Guid? LinkedAccountId { get; set; }
}

public class UpdateGoalBudgetRequest : CreateGoalBudgetRequest
{
    public bool IsActive { get; set; } = true;
}

public record GoalBudgetProgressDto(decimal Progress, decimal Target, int Percentage);
```

- [ ] **Step 2: Implement `GoalBudgetsApiController.cs`** — mirror `CategoryBudgetsApiController` shape but without 409 handling:

```csharp
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/goal-budgets")]
public class GoalBudgetsApiController(IBudgetService budgetService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GoalBudgetListItemDto>>> Get(
        [FromQuery] string? currency = null,
        [FromQuery] string? type = null,
        [FromQuery] bool includeArchived = false)
    {
        try
        {
            var budgets = await budgetService.GetAllAsync(includeInactive: includeArchived, currency: currency, type: type);
            var dtos = new List<GoalBudgetListItemDto>();
            foreach (var b in budgets)
            {
                var progress = await budgetService.GetProgressAsync(b.Id);
                dtos.Add(new GoalBudgetListItemDto(
                    Id:                 b.Id,
                    Name:               b.Name,
                    GoalType:           b.GoalType,
                    CurrencyCode:       b.Currency.Code,
                    CurrencySymbol:     b.Currency.Symbol,
                    TargetAmount:       b.TargetAmount,
                    StartDate:          b.StartDate,
                    EndDate:            b.EndDate,
                    Description:        b.Description,
                    IsActive:           b.IsActive,
                    LinkedAccountId:    b.LinkedAccountId,
                    LinkedAccountName:  b.LinkedAccount?.Name,
                    Progress:           progress.AmountProgress));
            }
            return Ok(dtos);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = new { code = "INVALID_TYPE", message = ex.Message } });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoalBudgetEditDto>> GetById(Guid id)
    {
        var b = await budgetService.GetByIdAsync(id);
        if (b is null) return NotFound();
        return new GoalBudgetEditDto(
            Id:              b.Id,
            Name:            b.Name,
            GoalType:        b.GoalType,
            CurrencyId:      b.CurrencyId,
            CurrencyCode:    b.Currency.Code,
            TargetAmount:    b.TargetAmount,
            StartDate:       b.StartDate,
            EndDate:         b.EndDate,
            Description:     b.Description,
            IsActive:        b.IsActive,
            LinkedAccountId: b.LinkedAccountId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoalBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var vm = new BudgetCreateViewModel
        {
            Name            = request.Name,
            TargetAmount    = request.TargetAmount,
            CurrencyId      = request.CurrencyId,
            StartDate       = request.StartDate,
            EndDate         = request.EndDate,
            Description     = request.Description,
            GoalType        = request.GoalType,
            LinkedAccountId = request.LinkedAccountId
        };

        try
        {
            var budget = await budgetService.CreateAsync(vm);
            return Created($"/api/goal-budgets/{budget.Id}", new { id = budget.Id });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGoalBudgetRequest request)
    {
        if (!ModelState.IsValid) return UnprocessableEntity(ToValidationEnvelope());

        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();

        var vm = new BudgetEditViewModel
        {
            Id              = id,
            Name            = request.Name,
            TargetAmount    = request.TargetAmount,
            CurrencyId      = request.CurrencyId,
            StartDate       = request.StartDate,
            EndDate         = request.EndDate,
            Description     = request.Description,
            GoalType        = request.GoalType,
            LinkedAccountId = request.LinkedAccountId
        };

        try
        {
            await budgetService.UpdateAsync(vm);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = new { code = "VALIDATION_ERROR", message = ex.Message, details = Array.Empty<object>() } });
        }
    }

    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await budgetService.DeactivateAsync(id);
        return NoContent();
    }

    [HttpPatch("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await budgetService.ReactivateAsync(id);
        return NoContent();
    }

    [HttpGet("{id:guid}/progress")]
    public async Task<ActionResult<GoalBudgetProgressDto>> GetProgress(Guid id)
    {
        var existing = await budgetService.GetByIdAsync(id);
        if (existing is null) return NotFound();
        var p = await budgetService.GetProgressAsync(id);
        var pct = p.TargetAmount == 0m ? 0 : (int)Math.Round(p.AmountProgress / p.TargetAmount * 100m);
        return new GoalBudgetProgressDto(p.AmountProgress, p.TargetAmount, pct);
    }

    private object ToValidationEnvelope() => new
    {
        error = new
        {
            code    = "VALIDATION_ERROR",
            message = "Validation failed.",
            details = ModelState
                .Where(kv => kv.Value!.Errors.Count > 0)
                .SelectMany(kv => kv.Value!.Errors.Select(e => new { field = kv.Key, message = e.ErrorMessage }))
                .ToArray()
        }
    };
}
```

- [ ] **Step 3: Write `GoalBudgetsCrudApiTests.cs`** — covers POST (Spending, Savings), GET, PUT, archive, reactivate, EndDate < StartDate validation, INVALID_TYPE filter:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class GoalBudgetsCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededBudgetIds = [];

    public GoalBudgetsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededBudgetIds.Count > 0)
            await db.Budgets.Where(b => _seededBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Post_creates_spending_goal()
    {
        var response = await _client.PostAsJsonAsync("/api/goal-budgets", new
        {
            name         = $"Trip-{Guid.NewGuid():N}",
            goalType     = "Spending",
            currencyId   = 1,
            targetAmount = 1500m,
            startDate    = "2026-01-01"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);
    }

    [Fact]
    public async Task Post_creates_savings_goal_with_inferred_currency()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Sav-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);

        var response = await _client.PostAsJsonAsync("/api/goal-budgets", new
        {
            name            = $"Emergency-{Guid.NewGuid():N}",
            goalType        = "Savings",
            targetAmount    = 5000m,
            startDate       = "2026-01-01",
            linkedAccountId = account.Id
            // No currencyId — server derives from the linked account.
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var fresh = await db.Budgets.FindAsync(id);
        fresh!.CurrencyId.Should().Be(1);
    }

    [Fact]
    public async Task Post_returns_422_when_endDate_before_startDate()
    {
        var response = await _client.PostAsJsonAsync("/api/goal-budgets", new
        {
            name         = $"Bad-{Guid.NewGuid():N}",
            goalType     = "Spending",
            currencyId   = 1,
            targetAmount = 100m,
            startDate    = "2026-06-01",
            endDate      = "2026-05-01"
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetList_with_invalid_type_returns_400()
    {
        var response = await _client.GetAsync("/api/goal-budgets?type=banana");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Archive_then_reactivate_round_trip()
    {
        var create = await _client.PostAsJsonAsync("/api/goal-budgets", new
        {
            name         = $"Trip-{Guid.NewGuid():N}",
            goalType     = "Spending",
            currencyId   = 1,
            targetAmount = 100m,
            startDate    = "2026-01-01"
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        (await _client.PatchAsync($"/api/goal-budgets/{id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.PatchAsync($"/api/goal-budgets/{id}/reactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Budgets.FindAsync(id))!.IsActive.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Write `GoalBudgetsProgressApiTests.cs`** — verifies Spending progress sums tagged transactions and Savings progress reads account balance:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class GoalBudgetsProgressApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededBudgetIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public GoalBudgetsProgressApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededBudgetIds.Count > 0)
            await db.Budgets.Where(b => _seededBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Spending_goal_progress_sums_tagged_transactions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Sp-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        await db.SaveChangesAsync();

        var goal = new Budget { Id = Guid.NewGuid(), Name = "T", TargetAmount = 1000m, CurrencyId = 1, StartDate = new DateOnly(2026, 1, 1), GoalType = "Spending", IsActive = true };
        db.Budgets.Add(goal);
        _seededBudgetIds.Add(goal.Id);

        var t1 = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId, Date = new DateOnly(2026, 2, 1), Amount = 200m, BudgetId = goal.Id };
        var t2 = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId, Date = new DateOnly(2026, 3, 1), Amount = 150m, BudgetId = goal.Id };
        db.Transactions.AddRange(t1, t2);
        _seededTransactionIds.AddRange([t1.Id, t2.Id]);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/goal-budgets/{goal.Id}/progress");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("progress").GetDecimal().Should().Be(350m);
        dto.GetProperty("target").GetDecimal().Should().Be(1000m);
        dto.GetProperty("percentage").GetInt32().Should().Be(35);
    }
}
```

(Savings-progress test is symmetric. Add it if test count for the file is sparse, or skip — `IBudgetService.GetProgressAsync` is exercised by the Spending case and existing dashboard tests cover Savings via `accountService.GetBalanceAsync`.)

- [ ] **Step 5: Run tests, expect all pass**

```bash
dotnet test ProjectCeres.Tests
```

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/ViewModels/GoalBudgetCrudDtos.cs \
        ProjectCeres/Controllers/Api/GoalBudgetsApiController.cs \
        ProjectCeres.Tests/Integration/Api/GoalBudgetsCrudApiTests.cs \
        ProjectCeres.Tests/Integration/Api/GoalBudgetsProgressApiTests.cs
git commit -m "feat(api): GoalBudgetsApiController — list, CRUD, archive/reactivate, progress"
```

---

## Task 8: Server — `BudgetsApiController` discriminator + tests

**Files:**
- Create: `ProjectCeres/ViewModels/BudgetDiscriminatorDto.cs`
- Create: `ProjectCeres/Controllers/Api/BudgetsApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/BudgetsDiscriminatorApiTests.cs`

- [ ] **Step 1: Define DTO**

```csharp
namespace ProjectCeres.ViewModels;

public record BudgetDiscriminatorDto(Guid Id, string Kind);
```

- [ ] **Step 2: Implement controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/budgets")]
public class BudgetsApiController(AppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BudgetDiscriminatorDto>> GetKind(Guid id)
    {
        if (await db.CategoryBudgets.AnyAsync(cb => cb.Id == id))
            return new BudgetDiscriminatorDto(id, "CategoryBudget");

        if (await db.Budgets.AnyAsync(b => b.Id == id))
            return new BudgetDiscriminatorDto(id, "GoalBudget");

        return NotFound();
    }
}
```

- [ ] **Step 3: Write `BudgetsDiscriminatorApiTests.cs`** — three tests: 404, CategoryBudget, GoalBudget. Same fixture-cleanup pattern as Task 7.

- [ ] **Step 4: Run tests + commit**

```bash
dotnet test ProjectCeres.Tests
git add ProjectCeres/ViewModels/BudgetDiscriminatorDto.cs \
        ProjectCeres/Controllers/Api/BudgetsApiController.cs \
        ProjectCeres.Tests/Integration/Api/BudgetsDiscriminatorApiTests.cs
git commit -m "feat(api): BudgetsApiController discriminator endpoint"
```

---

## Task 9: SPA — `useSettings` types + Settings page UI

**Files:**
- Modify: `ProjectCeres.Client/src/app/lib/use-settings.ts`
- Modify: `ProjectCeres.Client/src/app/pages/Settings.tsx`
- Modify: `ProjectCeres.Client/src/app/pages/Settings.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/budget-display.ts`
- Create: `ProjectCeres.Client/src/app/features/budgets/budget-display.test.ts`

- [ ] **Step 1: Extend `AppSettings` type and FALLBACK** in `use-settings.ts`:

```typescript
export type AppSettings = {
  numberFormat: 'comma_decimal' | 'period_decimal';
  dateFormat: string;
  defaultCurrencyCode: string;
  defaultCurrencySymbol: string;
  budgetPeriodStartDay: number;
};

const FALLBACK: AppSettings = {
  numberFormat: 'period_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyCode: '',
  defaultCurrencySymbol: '',
  budgetPeriodStartDay: 1,
};
```

- [ ] **Step 2: Implement `budget-display.ts`** with the ordinal helper:

```typescript
import type { Budget } from './budgets-api';

export const GOAL_TYPE_LABEL: Record<'Spending' | 'Savings', string> = {
  Spending: 'Spending',
  Savings: 'Savings',
};

/**
 * English ordinal suffix for the Settings save toast.
 * 1 → '1st', 2 → '2nd', 3 → '3rd', 4 → '4th', 11 → '11th', 21 → '21st', etc.
 */
export function ordinal(n: number): string {
  const mod100 = n % 100;
  if (mod100 >= 11 && mod100 <= 13) return `${n}th`;
  switch (n % 10) {
    case 1: return `${n}st`;
    case 2: return `${n}nd`;
    case 3: return `${n}rd`;
    default: return `${n}th`;
  }
}
```

(Drop the `import type { Budget }` if you don't need it here yet — tasks 10/11 will define it. The `ordinal` function is used in this task.)

- [ ] **Step 3: Test `ordinal`** in `budget-display.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import { ordinal } from './budget-display';

describe('ordinal', () => {
  it.each([
    [1, '1st'], [2, '2nd'], [3, '3rd'], [4, '4th'], [10, '10th'],
    [11, '11th'], [12, '12th'], [13, '13th'],
    [21, '21st'], [22, '22nd'], [23, '23rd'], [24, '24th'],
    [31, '31st'],
  ])('formats %i as %s', (input, expected) => {
    expect(ordinal(input)).toBe(expected);
  });
});
```

- [ ] **Step 4: Run — expect PASS**

```bash
pnpm --dir ProjectCeres.Client test budget-display --run
```

- [ ] **Step 5: Update Settings page** — read existing `pages/Settings.tsx`. Add a new field block after Default Currency. Use whatever pattern that page uses for the existing fields. Pseudocode:

```tsx
import { ordinal } from '../features/budgets/budget-display';

// inside the form:
<div className="space-y-1.5">
  <Label htmlFor="settings-period-start">Budget period start day</Label>
  <Input
    id="settings-period-start"
    type="number"
    min={1}
    max={31}
    value={periodStartDay}
    onChange={(e) => setPeriodStartDay(Number(e.target.value))}
  />
  <p className="text-xs text-muted-foreground">
    All your budget cycles start on this day. Use 1 for calendar months. If a
    month doesn't have this day (e.g., 31 in April), the cycle starts on that
    month's last day.
  </p>
</div>
```

On save, after the existing PATCH succeeds:

```tsx
toast.success(`Saved. Budget periods now start on the ${ordinal(periodStartDay)}.`);
```

(Read the existing Settings page to understand exactly which save handler to extend. The Settings SPA migration is still partial — the page may still POST to a Razor URL via `<form>`. If so, add the field but route the SAVE through the `PATCH /api/settings` endpoint that does exist.)

- [ ] **Step 6: Update `Settings.test.tsx`** — assert the new field renders + the toast text uses the ordinal.

- [ ] **Step 7: Run client tests + build**

```bash
pnpm --dir ProjectCeres.Client test --run
pnpm --dir ProjectCeres.Client build
```

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres.Client/src/app/lib/use-settings.ts \
        ProjectCeres.Client/src/app/pages/Settings.tsx \
        ProjectCeres.Client/src/app/pages/Settings.test.tsx \
        ProjectCeres.Client/src/app/features/budgets/budget-display.ts \
        ProjectCeres.Client/src/app/features/budgets/budget-display.test.ts
git commit -m "feat(spa): Settings UI for BudgetPeriodStartDay + ordinal helper"
```

---

## Task 10: SPA — `budgets-api.ts` URL builders + DTO types

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/budgets-api.ts`

- [ ] **Step 1: Define everything the SPA will need**

```typescript
// ---------- URL builders ----------

export const CATEGORY_BUDGETS_URL = '/api/category-budgets';
export const CATEGORY_BUDGET_BY_ID_URL = (id: string) => `/api/category-budgets/${id}`;
export const CATEGORY_BUDGET_ARCHIVE_URL = (id: string) => `/api/category-budgets/${id}/archive`;
export const CATEGORY_BUDGET_REACTIVATE_URL = (id: string) => `/api/category-budgets/${id}/reactivate`;
export const CATEGORY_BUDGET_SPEND_URL = (id: string, year: number, month: number) =>
  `/api/category-budgets/${id}/spend?year=${year}&month=${month}`;

export const GOAL_BUDGETS_URL = '/api/goal-budgets';
export const GOAL_BUDGET_BY_ID_URL = (id: string) => `/api/goal-budgets/${id}`;
export const GOAL_BUDGET_ARCHIVE_URL = (id: string) => `/api/goal-budgets/${id}/archive`;
export const GOAL_BUDGET_REACTIVATE_URL = (id: string) => `/api/goal-budgets/${id}/reactivate`;
export const GOAL_BUDGET_PROGRESS_URL = (id: string) => `/api/goal-budgets/${id}/progress`;

export const BUDGET_DISCRIMINATOR_URL = (id: string) => `/api/budgets/${id}`;

// ---------- DTOs ----------

export type CategoryBudgetListItemDto = {
  id: string;
  categoryId: string;
  categoryName: string;
  currencyCode: string;
  currencySymbol: string;
  limitAmount: number;
  isActive: boolean;
  currentPeriodSpend: number;
  currentPeriodEnd: string; // yyyy-MM-dd
};

export type CategoryBudgetEditDto = {
  id: string;
  categoryId: string;
  currencyId: number;
  currencyCode: string;
  limitAmount: number;
  isActive: boolean;
};

export type CreateCategoryBudgetRequest = {
  categoryId: string;
  currencyId: number;
  limitAmount: number;
};

export type UpdateCategoryBudgetRequest = CreateCategoryBudgetRequest & { isActive: boolean };

export type GoalType = 'Spending' | 'Savings';

export type GoalBudgetListItemDto = {
  id: string;
  name: string;
  goalType: GoalType;
  currencyCode: string;
  currencySymbol: string;
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  isActive: boolean;
  linkedAccountId: string | null;
  linkedAccountName: string | null;
  progress: number;
};

export type GoalBudgetEditDto = {
  id: string;
  name: string;
  goalType: GoalType;
  currencyId: number;
  currencyCode: string;
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  isActive: boolean;
  linkedAccountId: string | null;
};

export type CreateGoalBudgetRequest = {
  name: string;
  goalType: GoalType;
  currencyId: number | null;       // null when goalType=Savings
  targetAmount: number;
  startDate: string;
  endDate: string | null;
  description: string | null;
  linkedAccountId: string | null;  // required when goalType=Savings
};

export type UpdateGoalBudgetRequest = CreateGoalBudgetRequest & { isActive: boolean };

export type BudgetKind = 'CategoryBudget' | 'GoalBudget';
export type BudgetDiscriminatorDto = { id: string; kind: BudgetKind };

// ---------- DUPLICATE_BUDGET 409 envelope shape ----------

export type DuplicateBudgetEnvelope = {
  error: {
    code: 'DUPLICATE_BUDGET';
    message: string;
    existingBudgetId: string;
    existingIsActive: boolean;
  };
};
```

- [ ] **Step 2: Build to verify TS**

```bash
pnpm --dir ProjectCeres.Client build
```

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/features/budgets/budgets-api.ts
git commit -m "feat(spa): budgets-api URL builders + DTO types"
```

---

## Task 11: SPA — `BudgetProgressBar` shared primitive

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetProgressBar.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetProgressBar.test.tsx`

- [ ] **Step 1: Implement**

```tsx
import { Numeric } from '@/components/Numeric';
import { Progress } from '@/components/ui/progress';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  progress: number;
  target: number;
  currencySymbol: string;
};

export function BudgetProgressBar({ progress, target, currencySymbol }: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const percent = target > 0 ? Math.min(100, Math.round((progress / target) * 100)) : 0;

  // Color tier: <70% green, 70–99% amber, ≥100% destructive.
  const colorClass =
    percent >= 100
      ? 'bg-destructive'
      : percent >= 70
        ? 'bg-warning'
        : 'bg-success';

  return (
    <div className="space-y-1">
      <div className="flex justify-between text-xs">
        <Numeric>{currencySymbol} {formatNumberForDisplay(progress, numberFormat)}</Numeric>
        <Numeric className="text-muted-foreground">{currencySymbol} {formatNumberForDisplay(target, numberFormat)}</Numeric>
      </div>
      <Progress value={percent} className="h-2" indicatorClassName={colorClass} />
    </div>
  );
}
```

(If `Progress` doesn't accept `indicatorClassName`, inspect `@/components/ui/progress.tsx` and adjust. The shadcn pattern usually has `<ProgressIndicator className={...}>`.)

- [ ] **Step 2: Test** — verify percent calculation and color tier:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { BudgetProgressBar } from './BudgetProgressBar';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal' }, loading: false }),
}));

describe('BudgetProgressBar', () => {
  it('shows progress and target amounts with the symbol', () => {
    render(<BudgetProgressBar progress={250} target={1000} currencySymbol="€" />);
    expect(screen.getByText(/€ 250\.00/)).toBeInTheDocument();
    expect(screen.getByText(/€ 1,000\.00/)).toBeInTheDocument();
  });

  it('caps percent at 100 visually', () => {
    const { container } = render(<BudgetProgressBar progress={2000} target={1000} currencySymbol="$" />);
    const indicator = container.querySelector('[role="progressbar"]')!;
    expect(indicator.getAttribute('aria-valuenow')).toBe('100');
  });
});
```

- [ ] **Step 3: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test BudgetProgressBar --run
git add ProjectCeres.Client/src/app/features/budgets/BudgetProgressBar.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetProgressBar.test.tsx
git commit -m "feat(spa): BudgetProgressBar shared primitive"
```

---

## Task 12: SPA — `CurrencyCombobox` shared primitive

**Files:**
- Create: `ProjectCeres.Client/src/components/CurrencyCombobox.tsx`
- Create: `ProjectCeres.Client/src/components/CurrencyCombobox.test.tsx`

A new picker for budget forms. Loads currencies from a new (or existing) endpoint. Check first whether `/api/currencies` exists. If not, the component reads currencies from `/api/accounts/active` (deduplicating by code) — since the data is already shipped to the SPA, no new endpoint is needed in this plan.

- [ ] **Step 1: Implement** (using accounts as the currency source):

```tsx
import { useMemo } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useApi } from '@/app/lib/use-api';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from '@/app/features/movements/movements-api';

type Currency = { id: number; code: string; symbol: string };

type Props = {
  value: number | null;
  onChange: (currencyId: number | null, currency: Currency | null) => void;
  placeholder?: string;
};

export function CurrencyCombobox({ value, onChange, placeholder = 'Select currency' }: Props) {
  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);

  const currencies = useMemo<Currency[]>(() => {
    const seen = new Map<string, Currency>();
    for (const a of accounts ?? []) {
      if (!seen.has(a.currencyCode)) {
        // The accounts endpoint doesn't expose currencyId today. We can't pick by id without it.
        // A real implementation needs a /api/currencies endpoint OR the accounts endpoint to include currencyId.
        // For now, infer id by code → look it up server-side at submit time. (Adjust at implementation.)
        seen.set(a.currencyCode, { id: 0, code: a.currencyCode, symbol: a.currencySymbol });
      }
    }
    return Array.from(seen.values()).sort((a, b) => a.code.localeCompare(b.code));
  }, [accounts]);

  const selected = currencies.find((c) => c.code === currencies.find((x) => x.id === value)?.code);
  // ... combobox UI ...
}
```

**Stop. This block is unworkable** — the accounts endpoint doesn't expose `currencyId`, only `currencyCode`. The form needs `CurrencyId` for the API request body.

**Mitigation: introduce a lightweight `/api/currencies` endpoint as part of this task.** Add to the server:

```csharp
// In a new ProjectCeres/Controllers/Api/CurrenciesApiController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/currencies")]
public class CurrenciesApiController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(await db.Currencies
            .OrderBy(c => c.Code)
            .Select(c => new { id = c.Id, code = c.Code, symbol = c.Symbol })
            .ToListAsync());
}
```

Add a 1-test integration check:

```csharp
[Fact]
public async Task GetCurrencies_returns_seed_data()
{
    var response = await _client.GetAsync("/api/currencies");
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadAsStringAsync();
    body.Should().Contain("EUR");
}
```

- [ ] **Step 2: Implement `CurrencyCombobox.tsx`** properly:

```tsx
import { useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useApi } from '@/app/lib/use-api';

type Currency = { id: number; code: string; symbol: string };

type Props = {
  value: number | null;
  onChange: (id: number | null) => void;
  placeholder?: string;
  disabled?: boolean;
};

export function CurrencyCombobox({ value, onChange, placeholder = 'Select currency', disabled }: Props) {
  const [open, setOpen] = useState(false);
  const { data: currencies } = useApi<Currency[]>('/api/currencies');
  const list = currencies ?? [];
  const selected = list.find((c) => c.id === value) ?? null;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            disabled={disabled}
            className={cn('w-full justify-between font-normal', !selected && 'text-muted-foreground')}
          >
            {selected ? `${selected.symbol} ${selected.code}` : placeholder}
            <ChevronsUpDown className="ml-2 h-4 w-4 opacity-50" />
          </Button>
        }
      />
      <PopoverContent align="start" className="w-(--radix-popover-trigger-width) p-0">
        <Command>
          <CommandInput placeholder="Search currencies…" />
          <CommandList>
            <CommandEmpty>No currencies.</CommandEmpty>
            <CommandGroup>
              {list.map((c) => (
                <CommandItem key={c.id} value={c.code} onSelect={() => { onChange(c.id); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', value === c.id ? 'opacity-100' : 'opacity-0')} />
                  {c.symbol} {c.code}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 3: Test** — assert it renders, lets the user pick, fires onChange:

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CurrencyCombobox } from './CurrencyCombobox';

const mockFetch = vi.fn();
beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => [
      { id: 1, code: 'EUR', symbol: '€' },
      { id: 2, code: 'USD', symbol: '$' },
    ],
  });
});
afterEach(() => vi.resetAllMocks());

describe('CurrencyCombobox', () => {
  it('lists currencies and fires onChange on select', async () => {
    const onChange = vi.fn();
    render(<CurrencyCombobox value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText(/USD/)).toBeInTheDocument());
    fireEvent.click(screen.getByText(/USD/));
    expect(onChange).toHaveBeenCalledWith(2);
  });
});
```

- [ ] **Step 4: Build + run all client tests + commit**

```bash
pnpm --dir ProjectCeres.Client test --run
pnpm --dir ProjectCeres.Client build
dotnet test ProjectCeres.Tests
git add ProjectCeres.Client/src/components/CurrencyCombobox.tsx \
        ProjectCeres.Client/src/components/CurrencyCombobox.test.tsx \
        ProjectCeres/Controllers/Api/CurrenciesApiController.cs \
        ProjectCeres.Tests/Integration/Api/  # new test file
git commit -m "feat(spa): CurrencyCombobox primitive + /api/currencies endpoint"
```

---

## Task 13: SPA — `BudgetRowMenu` component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.test.tsx`

A `⋯` menu for budget rows. Edit · Archive (or Reactivate). Pattern is identical to `MovementRowMenu.tsx` — copy that file and adapt.

- [ ] **Step 1: Read `MovementRowMenu.tsx`** to mirror exactly.
- [ ] **Step 2: Implement** — accepts `{ kind: BudgetKind, id: string, isActive: boolean, onChanged: () => void }` props. On Edit, navigates to `/budgets/{id}/edit`. On Archive, opens AlertDialog → confirm → PATCH archive → toast → onChanged. On Reactivate, single-click → PATCH reactivate → toast → onChanged.
- [ ] **Step 3: Tests** — 4 tests: opens menu, edit navigates, archive confirms then PATCHes, reactivate is single-click.
- [ ] **Step 4: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test BudgetRowMenu --run
git add ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetRowMenu.test.tsx
git commit -m "feat(spa): BudgetRowMenu (edit / archive / reactivate)"
```

---

## Task 14: SPA — `CategoryBudgetsTable` + `GoalBudgetsTable`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.test.tsx`

Both follow `MovementsTable.tsx` shape — read it first.

- [ ] **Step 1: Implement `CategoryBudgetsTable.tsx`** — props `{ items: CategoryBudgetListItemDto[]; onChanged: () => void }`. Columns per spec §3.1. Uses `BudgetProgressBar` for the Progress column. Archived rows: `opacity-60` + `Archived` badge.
- [ ] **Step 2: Test** — 3 tests: renders rows, formats Spent/Limit using `formatNumberForDisplay`, shows Archived badge when `isActive=false`.
- [ ] **Step 3: Implement `GoalBudgetsTable.tsx`** — props `{ items: GoalBudgetListItemDto[]; onChanged: () => void }`. Columns per spec §3.2. Type badge: `bg-chart-{6|7}/10 text-chart-{6|7}` (Spending=6, Savings=7 or vice versa — pick one and stay consistent). End date renders via `formatDate(date, settings.dateFormat)` from `lib/date-format`, or `—` when null.
- [ ] **Step 4: Test** — same shape as Category, 3 tests.
- [ ] **Step 5: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test BudgetsTable --run
git add ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.tsx \
        ProjectCeres.Client/src/app/features/budgets/CategoryBudgetsTable.test.tsx \
        ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.tsx \
        ProjectCeres.Client/src/app/features/budgets/GoalBudgetsTable.test.tsx
git commit -m "feat(spa): CategoryBudgetsTable + GoalBudgetsTable"
```

---

## Task 15: SPA — `CategoryBudgetForm` + `GoalBudgetForm`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.test.tsx`

These mirror `MovementForm.tsx` shape — read it first.

- [ ] **Step 1: `CategoryBudgetForm.tsx`** props `{ mode: 'create'|'edit', initialValues, categories, onSubmit, onCancel }`. Fields per spec §4.1. `(budgeted)` suffix on the CategoryCombobox: pass a custom `renderItem` that appends the suffix when the category id is in a `budgetedCategoryIds` Set passed via prop. (Compute the set by fetching `/api/category-budgets?currency=X&includeArchived=true` when the user changes currency. Show as text suffix; do NOT disable the option — server enforces.)
- [ ] **Step 2: `CategoryBudgetForm.test.tsx`** — 4 tests: renders fields, fills + submits, shows the `(budgeted)` suffix, surfaces 422 errors.
- [ ] **Step 3: `GoalBudgetForm.tsx`** props `{ mode, initialValues, goalType, accounts, onSubmit, onCancel }`. Fields per spec §4.2 / §4.3 — pick rendering based on `goalType`. Spending form has CurrencyCombobox; Savings form has AccountCombobox (filter to Asset accounts) and reads currency from selected account. Inline error on EndDate when `< StartDate`.
- [ ] **Step 4: `GoalBudgetForm.test.tsx`** — 5 tests: renders Spending fields, renders Savings fields (different from Spending), submits Spending POST, submits Savings POST without currencyId, blocks submit when EndDate < StartDate.
- [ ] **Step 5: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test BudgetForm --run
pnpm --dir ProjectCeres.Client build
git add ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.tsx \
        ProjectCeres.Client/src/app/features/budgets/CategoryBudgetForm.test.tsx \
        ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.tsx \
        ProjectCeres.Client/src/app/features/budgets/GoalBudgetForm.test.tsx
git commit -m "feat(spa): CategoryBudgetForm + GoalBudgetForm"
```

---

## Task 16: SPA — `BudgetCreate` + `BudgetEdit` routed pages

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetCreate.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetCreate.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetEdit.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetEdit.test.tsx`

These mirror `MovementCreate.tsx` / `MovementEdit.tsx`.

- [ ] **Step 1: `BudgetCreate.tsx`** — reads `?type=` (`category`/`spending`/`savings`), bounces to `/budgets` if missing or invalid. Renders the right form. Submit POSTs to the right endpoint. On 201, navigate to `/budgets?type={category|goal}`. **On 409 (DUPLICATE_BUDGET)**, render a `_form`-level prompt:

```tsx
{conflict && (
  <div className="rounded-md border border-warning/30 bg-warning/10 p-4 text-sm">
    {conflict.existingIsActive ? (
      <>This combination already has an active budget. <Link to={`/budgets/${conflict.existingBudgetId}/edit`}>Edit it instead</Link>.</>
    ) : (
      <>An archived budget for this category and currency exists. <button type="button" onClick={() => reactivateAndRedirect(conflict.existingBudgetId)}>Reactivate it</button>.</>
    )}
  </div>
)}
```

`reactivateAndRedirect`: PATCH reactivate → on 204, navigate to `/budgets/{id}/edit`.

- [ ] **Step 2: `BudgetCreate.test.tsx`** — 5 tests: renders Category form for `?type=category`, renders Spending form for `?type=spending`, renders Savings form for `?type=savings`, bounces to `/budgets` for missing param, surfaces 409 prompt with correct copy.
- [ ] **Step 3: `BudgetEdit.tsx`** — params `:id`. Fetches `/api/budgets/{id}` (discriminator) → branches to either CategoryBudget or GoalBudget edit flow. Loads typed DTO from the right endpoint. Renders the right form pre-filled. Save → PUT. Navigate back to `/budgets?type={category|goal}` on success.
- [ ] **Step 4: `BudgetEdit.test.tsx`** — 4 tests: discriminator routes Category vs Goal, prefill works for each, save+navigate works, 404 on bad id renders "not found" link.
- [ ] **Step 5: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test BudgetCreate BudgetEdit --run
git add ProjectCeres.Client/src/app/features/budgets/BudgetCreate.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetCreate.test.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetEdit.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetEdit.test.tsx
git commit -m "feat(spa): BudgetCreate + BudgetEdit routed pages with discriminator"
```

---

## Task 17: SPA — `BudgetsLayout` (page shell, tabs, +New, archive toggle)

**Files:**
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx`
- Create: `ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.test.tsx`

Mirrors `MovementsLayout.tsx` shape — read it first.

- [ ] **Step 1: `BudgetsLayout.tsx`**:

```tsx
import { useEffect, useRef } from 'react';
import { Outlet, useMatch, useNavigate, useSearchParams } from 'react-router-dom';
import { Plus, ChevronDown, Wallet, Trophy, PiggyBank } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Switch } from '@/components/ui/switch';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { CategoryBudgetsTable } from './CategoryBudgetsTable';
import { GoalBudgetsTable } from './GoalBudgetsTable';
import {
  CATEGORY_BUDGETS_URL,
  GOAL_BUDGETS_URL,
  type CategoryBudgetListItemDto,
  type GoalBudgetListItemDto,
} from './budgets-api';
import { useApi } from '../../lib/use-api';

type Tab = 'category' | 'goal';

export function BudgetsLayout() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const onCreate = !!useMatch('/budgets/new');
  const onEdit   = !!useMatch('/budgets/:id/edit');

  if (onCreate || onEdit) {
    return <div className="space-y-6"><Outlet /></div>;
  }

  const tab = (params.get('type') ?? 'category') as Tab;
  const includeArchived = params.get('includeArchived') === 'true';

  const url = tab === 'category'
    ? `${CATEGORY_BUDGETS_URL}${includeArchived ? '?includeArchived=true' : ''}`
    : `${GOAL_BUDGETS_URL}${includeArchived ? '?includeArchived=true' : ''}`;

  const categoryQuery = useApi<CategoryBudgetListItemDto[]>(tab === 'category' ? url : null!);
  const goalQuery     = useApi<GoalBudgetListItemDto[]>(tab === 'goal' ? url : null!);

  function setTab(next: Tab) {
    const p = new URLSearchParams(params);
    p.set('type', next);
    setParams(p, { replace: true });
  }

  function setIncludeArchived(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeArchived', 'true');
    else p.delete('includeArchived');
    setParams(p, { replace: true });
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-3xl font-semibold">Budgets</h1>
        <DropdownMenu>
          <DropdownMenuTrigger render={<Button className="gap-2"><Plus className="h-4 w-4" /> New <ChevronDown className="h-4 w-4 opacity-70" /></Button>} />
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={() => navigate('new?type=category')} title="A monthly cap on spending in a specific category (e.g., €600/month on groceries).">
              <Wallet className="mr-2 h-4 w-4 text-muted-foreground" /> Category Budget
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => navigate('new?type=spending')} title="Track money you're spending toward a target (e.g., a trip, a renovation). Tagged transactions count toward progress.">
              <Trophy className="mr-2 h-4 w-4 text-muted-foreground" /> Spending Goal
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => navigate('new?type=savings')} title="Track money accumulated in a designated account (e.g., emergency fund). Progress = current account balance.">
              <PiggyBank className="mr-2 h-4 w-4 text-muted-foreground" /> Savings Goal
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <Tabs value={tab} onValueChange={(v) => setTab(v as Tab)}>
        <TabsList>
          <TabsTrigger value="category">Category Budgets</TabsTrigger>
          <TabsTrigger value="goal">Goal Budgets</TabsTrigger>
        </TabsList>
      </Tabs>

      <div className="flex items-center gap-2">
        <Switch checked={includeArchived} onCheckedChange={setIncludeArchived} id="show-archived" />
        <label htmlFor="show-archived" className="text-sm">Show archived</label>
      </div>

      {tab === 'category' && (
        <>
          {categoryQuery.loading && <Skeleton className="h-[300px] w-full" />}
          {categoryQuery.error && <CardError section="Category Budgets" onRetry={categoryQuery.refetch} />}
          {categoryQuery.data && categoryQuery.data.length === 0 && <p className="text-sm text-muted-foreground">No category budgets.</p>}
          {categoryQuery.data && categoryQuery.data.length > 0 && (
            <CategoryBudgetsTable items={categoryQuery.data} onChanged={categoryQuery.refetch} />
          )}
        </>
      )}

      {tab === 'goal' && (
        <>
          {goalQuery.loading && <Skeleton className="h-[300px] w-full" />}
          {goalQuery.error && <CardError section="Goal Budgets" onRetry={goalQuery.refetch} />}
          {goalQuery.data && goalQuery.data.length === 0 && <p className="text-sm text-muted-foreground">No goal budgets.</p>}
          {goalQuery.data && goalQuery.data.length > 0 && (
            <GoalBudgetsTable items={goalQuery.data} onChanged={goalQuery.refetch} />
          )}
        </>
      )}
    </div>
  );
}
```

(The `useApi` calls with `null!` to skip fetching are an antipattern — adapt to whatever `useApi` actually supports. If `useApi` doesn't support skipping, two separate `useApi` calls always fetch both lists; UI hides the inactive one. Performance impact: one extra fetch per page load — acceptable for an internal beta.)

- [ ] **Step 2: Test** — 5 tests: renders heading + tabs, switching tab updates URL, +New dropdown items navigate with correct query, Show-archived toggle updates URL, refetch fires on data change.
- [ ] **Step 3: Update `pages/Budgets.tsx`** to render `<BudgetsLayout />` instead of the placeholder.
- [ ] **Step 4: Add the routes** to wherever the app declares them. Read `AppLayout.tsx` (or equivalent) and find how `/movements/*` is routed. Add:

```tsx
<Route path="budgets" element={<BudgetsLayout />}>
  <Route path="new" element={<BudgetCreate />} />
  <Route path=":id/edit" element={<BudgetEdit />} />
</Route>
```

- [ ] **Step 5: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test --run
pnpm --dir ProjectCeres.Client build
git add ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.tsx \
        ProjectCeres.Client/src/app/features/budgets/BudgetsLayout.test.tsx \
        ProjectCeres.Client/src/app/pages/Budgets.tsx \
        # plus any AppLayout.tsx route changes
git commit -m "feat(spa): BudgetsLayout — tabs + dropdown + archive toggle, Budgets routes"
```

---

## Task 18: SPA — Movement form Spending-Goal picker

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementForm.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx`
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx` (already passes `budgetId`? Verify and fix.)
- Modify: `ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx`

Per spec §7. Conditional render: only when ≥1 active Spending Goal exists in the transaction's currency. Edit-mode preserves links to archived goals.

- [ ] **Step 1: Add to `MovementForm.tsx` (Transaction variant only)** — fetch active Spending Goals once at form mount via `useApi<GoalBudgetListItemDto[]>('/api/goal-budgets?type=spending')`. (Don't filter `includeArchived=true` for the dropdown — but DO append the currently-tagged-archived goal manually, see Step 2.)

  - Compute: the selected account's `currencyCode` (from `accounts` array via `values.accountId`).
  - Filter the goals list to: `goal.currencyCode === selectedCurrency && (goal.isActive || goal.id === values.budgetId)`.
  - If filter result is empty AND `values.budgetId` is null → DON'T render the field.
  - Otherwise render a Combobox-style picker `<BudgetCombobox>` inline (or use AccountCombobox-style approach) with options: `(None)` first, then each goal with `(archived)` suffix when `!goal.isActive`.

- [ ] **Step 2: Edit-mode handling for archived links** — fetch BOTH `?type=spending&includeArchived=true` so an archived currently-tagged goal appears, OR fetch active + (when `values.budgetId` exists) hit `GET /api/goal-budgets/{id}` separately and merge. Pick whichever is simpler. The `includeArchived=true` route is one extra fetch on Edit; tolerable.

- [ ] **Step 3: Update `MovementCreate.tsx` / `MovementEdit.tsx`** — verify `budgetId` is already in the request body. (It should be, per `MovementFormValues` already having the field.) If `null`, send `null`.

- [ ] **Step 4: Tests** in `MovementForm.test.tsx` — 4 tests:
  - Field NOT rendered when no Spending Goals match the transaction's currency.
  - Field IS rendered when ≥1 Spending Goal matches.
  - Selecting a goal updates `values.budgetId`.
  - Edit mode shows currently-tagged archived goal in the picker with `(archived)` suffix.

- [ ] **Step 5: Run + commit**

```bash
pnpm --dir ProjectCeres.Client test MovementForm --run
git add ProjectCeres.Client/src/app/features/movements/MovementForm.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementForm.test.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementCreate.tsx \
        ProjectCeres.Client/src/app/features/movements/MovementEdit.tsx
git commit -m "feat(spa): Movement form Spending-Goal picker (conditional, currency-aware)"
```

---

## Task 19: Razor demolition — `BudgetsController` → 302s, delete views

**Files:**
- Modify: `ProjectCeres/Controllers/BudgetsController.cs`
- Delete: `ProjectCeres/Views/Budgets/Index.cshtml`
- Delete: `ProjectCeres/Views/Budgets/Goals.cshtml`
- Delete: `ProjectCeres/Views/Budgets/Create.cshtml`
- Delete: `ProjectCeres/Views/Budgets/CreateGoal.cshtml`
- Delete: `ProjectCeres/Views/Budgets/Edit.cshtml`
- Delete: `ProjectCeres/Views/Budgets/EditGoal.cshtml`
- Delete: `ProjectCeres/Views/Budgets/Deactivate.cshtml`
- Delete: `ProjectCeres/Views/Budgets/DeactivateGoal.cshtml`

- [ ] **Step 1: Slim `BudgetsController.cs`**:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class BudgetsController : Controller
{
    [HttpGet] public IActionResult Index()         => Redirect("/app/budgets?type=category");
    [HttpGet] public IActionResult Goals()         => Redirect("/app/budgets?type=goal");
    [HttpGet] public IActionResult Create()        => Redirect("/app/budgets/new?type=category");
    [HttpGet] public IActionResult CreateGoal()    => Redirect("/app/budgets/new?type=spending");
    [HttpGet] public IActionResult Edit(Guid id)            => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult EditGoal(Guid id)        => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult Deactivate(Guid id)      => Redirect($"/app/budgets/{id}/edit");
    [HttpGet] public IActionResult DeactivateGoal(Guid id)  => Redirect($"/app/budgets/{id}/edit");
}
```

- [ ] **Step 2: Delete the eight view files**

```bash
git rm ProjectCeres/Views/Budgets/Index.cshtml \
       ProjectCeres/Views/Budgets/Goals.cshtml \
       ProjectCeres/Views/Budgets/Create.cshtml \
       ProjectCeres/Views/Budgets/CreateGoal.cshtml \
       ProjectCeres/Views/Budgets/Edit.cshtml \
       ProjectCeres/Views/Budgets/EditGoal.cshtml \
       ProjectCeres/Views/Budgets/Deactivate.cshtml \
       ProjectCeres/Views/Budgets/DeactivateGoal.cshtml
```

- [ ] **Step 3: Build + test** — `dotnet build` and `dotnet test ProjectCeres.Tests` should be clean. If Razor tests in `BudgetsControllerTests.cs` (if it exists) fail because they tested POST/View behavior, prune them like we did with `MovementsControllerTests` in Task 8 of Plan 3.

```bash
grep -rn "BudgetsController\|Views/Budgets" ProjectCeres.Tests/ 2>/dev/null
```

If matches exist, inspect each and either delete (if it tests demolished POST/View) or update (if it tests the redirect).

- [ ] **Step 4: Commit**

```bash
git add -A ProjectCeres/Controllers/BudgetsController.cs ProjectCeres/Views/Budgets/
git commit -m "refactor(razor): BudgetsController page actions → 302 to /app/budgets, delete views"
```

---

## Task 20: Doc sync

**Files:**
- Modify: `docs/models.md`
- Modify: `docs/api-contract.md`
- Modify: `docs/planning-phase3-spa-migration.md`
- Modify: `docs/planning-phase3.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: `docs/models.md`** — find the Settings table row for `BudgetPeriodStartDay` (line 680 today). Update:
  - Range from "1–28" to "1–31".
  - Drop "Phase 3" annotation; replace with a parenthetical "(implemented 2026-MM-DD)".
  - Note the fallback rule: "For shorter months (e.g., 31 in April), the cycle starts on that month's last day."
  - Find the `CategoryBudget.PeriodStartDay` row (line 559) — annotate it as "Deferred. Was planned as a per-budget override; the global `Settings.BudgetPeriodStartDay` ships first. The override can be added later without breaking existing data."

- [ ] **Step 2: `docs/api-contract.md`** — add the 15 new endpoints + the supporting `/api/currencies` endpoint. Match the file's existing endpoint-table style.

- [ ] **Step 3: `docs/planning-phase3-spa-migration.md`** §2 controller table — update `BudgetsController` row to **Migrated (2026-MM-DD)**. Update §5 route map: replace `/budgets/categories` and `/budgets/goals` with `/budgets`, `/budgets/new`, `/budgets/:id/edit`.

- [ ] **Step 4: `docs/planning-phase3.md`** §14 step 5 — add a ✓ entry for Budgets:

```
- ✓ **Budgets — full CRUD with archive lifecycle (2026-MM-DD).** Unified `/app/budgets` page with Category and Goal tabs. Settings.BudgetPeriodStartDay (1–31) implemented; CategoryBudget actual spend respects the configured cycle. Razor BudgetsController page actions 302-redirect to the SPA. Spec: `docs/superpowers/specs/2026-05-01-budgets-spa-design.md`. Plan: `docs/superpowers/plans/2026-05-01-budgets-spa-implementation.md`.
- Pending: Reports (Step 5b — separate plan).
```

- [ ] **Step 5: `CHANGELOG.md`** under `[Unreleased]`:

```markdown
### Added
- Full Budgets CRUD on the SPA at `/app/budgets`: tabbed list (Category / Goal), `+New` dropdown (Category Budget / Spending Goal / Savings Goal), routed Create + Edit pages with discriminator routing, archive lifecycle with one-click reactivate, "Show archived" toggle, conflict-aware Create flow that offers to reactivate existing archived budgets.
- `Settings.BudgetPeriodStartDay` (1–31, default 1) — all category-budget actual-spend math respects the configured cycle. UI lives in Settings page with helper text + ordinal save toast.
- 16 typed API endpoints under `/api/category-budgets`, `/api/goal-budgets`, `/api/budgets`, and `/api/currencies`.
- Movement form Spending-Goal picker (conditional, currency-aware) so transactions can be tagged toward Spending goals.

### Changed
- Razor `BudgetsController` page actions (Index, Goals, Create, CreateGoal, Edit, EditGoal, Deactivate, DeactivateGoal) now 302-redirect to the SPA at `/app/budgets/*`.
- `ICategoryBudgetService.GetActualSpendAsync(id, year, month)` semantics shift: (year, month) now identifies the period whose end falls in that calendar month, computed via `BudgetPeriod` helper.

### Removed
- Razor views for Budgets (8 files under `Views/Budgets/`).
```

- [ ] **Step 6: Commit**

```bash
git add docs/models.md docs/api-contract.md docs/planning-phase3-spa-migration.md docs/planning-phase3.md CHANGELOG.md
git commit -m "docs(sync): budgets SPA migration — models, api-contract, planning, CHANGELOG"
```

---

## Task 21: Final regression + manual smoke

**Files:** none (verification).

- [ ] **Step 1: Full client tests + build**

```bash
pnpm --dir ProjectCeres.Client test --run
pnpm --dir ProjectCeres.Client build
```

Expected: all green, build clean.

- [ ] **Step 2: Full server tests + build**

```bash
dotnet test ProjectCeres.Tests
dotnet build
```

Expected: all green, 0 warnings, 0 errors.

- [ ] **Step 3: Manual browser smoke**

Start the .NET app + Vite dev server. Walk through:

1. `/Budgets` → 302 → `/app/budgets?type=category`.
2. `/Budgets/Goals` → 302 → `/app/budgets?type=goal`.
3. `/Budgets/Create` → 302 → `/app/budgets/new?type=category`.
4. On `/app/budgets`, switch tabs — URL updates, list refetches.
5. Click `+ New ▾ → Category Budget` — form renders, hover the dropdown items to see tooltips.
6. Pick a category that already has a budget — verify `(budgeted)` suffix appears.
7. Submit — server returns 409, form shows the "Edit it instead" prompt.
8. Click `+ New ▾ → Spending Goal` — Spending form renders. Submit with EndDate before StartDate — inline error.
9. Click `+ New ▾ → Savings Goal` — Savings form renders. Pick an asset account — currency renders below as inferred. Submit.
10. On the Goal tab, archive a goal. Toggle "Show archived" — archived goal appears, `Reactivate` available.
11. Reactivate the goal — list updates, badge gone.
12. Open a transaction's Edit page on the Movements list. If you have an active Spending Goal in the transaction's currency, the `Budget (optional)` field appears. Tag the transaction.
13. Open Settings. Change Budget period start day to 25. Save → toast reads "Saved. Budget periods now start on the 25th."
14. Go back to Dashboard. Category budget cards now show progress for the period ending in the current calendar month (e.g., today is May 5 → period is Apr 25 – May 24). Spending should reflect transactions in that range.
15. Set start day back to 1; verify dashboard returns to calendar-month behavior.

If anything fails, fix in the smallest commit possible and re-run.

---

## What this plan ships

After Task 21, the Budgets surface is the Phase 3 SPA's fifth migrated feature area:

- 16 typed API endpoints (15 budget-related + 1 currencies).
- Single `/app/budgets` page with Category + Goal tabs, `+New` dropdown, archive toggle, archive lifecycle.
- Configurable `Settings.BudgetPeriodStartDay` (1–31, fallback to month's last day) drives all CategoryBudget actual-spend math via `BudgetPeriod` helper.
- Movement form gains conditional Spending-Goal picker.
- Razor `BudgetsController` page actions all 302-redirect; views deleted.

Pending after this slice (intentionally out of scope):
- Reports (Phase 3 step 5b — separate plan).
- Per-budget `CategoryBudget.PeriodStartDay` override.
- Auth (Phase 3 final group).
