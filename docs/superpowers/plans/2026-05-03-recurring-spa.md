# Recurring Transactions SPA — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `/app/recurring` placeholder with a full Recurring SPA (list, Create, Edit, Confirm, Dismiss, Archive, Reactivate), wire the topbar bell to show due+overdue reminders, and fix Weekly/Biweekly Snap-to-calendar-day scheduling on the server.

**Architecture:** Server-side changes ship first (policies, Snap fix, Reactivate endpoint, EstimatedAmount nullability, Dismiss body, Razor cutover); client-side work follows the established Batch 2 SPA-page template (recurring-api.ts → RecurringLayout → table/row/form components → page glues → TopBar wiring). All changes land in a single commit.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core / PostgreSQL (server); React 19 + Vite + TypeScript + shadcn/ui base-nova + Lucide + Tailwind v4 (client); xUnit + Moq + FluentAssertions (server tests); Vitest + React Testing Library (client tests).

**Spec:** `docs/superpowers/specs/2026-05-03-recurring-spa-design.md`

---

## File map

### Create — server

| Path | Purpose |
|---|---|
| `ProjectCeres/Services/RecurringTransactionPolicies.cs` | NEW — `ValidateSchedule(frequency, behaviour, dayOfPeriod)` returning `Result` |

### Modify — server

| Path | Changes |
|---|---|
| `ProjectCeres/Services/IRecurringTransactionService.cs` | Drop throwing CRUD declarations; add `TryReactivateAsync(Guid)`; change `TryDismissAsync` signature to `(Guid, DateOnly?)` |
| `ProjectCeres/Services/RecurringTransactionService.cs` | Drop throwing method bodies; add `TryReactivateAsync`; rewrite `SnapToCalendarDay` for Weekly+Biweekly; call `RecurringTransactionPolicies.ValidateSchedule` from `TryCreateAsync`/`TryUpdateAsync`; update `TryDismissAsync` signature |
| `ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs` | Add `Reactivate` action; update `Dismiss` to bind `[FromBody] DismissRecurringTransactionRequest?` and pass `nextDueDate` |
| `ProjectCeres/ViewModels/RecurringTransactionApiDtos.cs` | Make `EstimatedAmount` on Create/Update requests `decimal?`; drop `Range` lower-bound coupling; add `DismissRecurringTransactionRequest` record |
| `ProjectCeres/Controllers/RecurringTransactionsController.cs` | Slim to 7 redirect actions (302) |
| `ProjectCeres.Tests/Integration/RecurringTransactionPoliciesTests.cs` | NEW — 8 unit tests for `ValidateSchedule` |
| `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs` | Drop throwing method tests; add 5 Weekly/Biweekly Snap tests; add 3 Reactivate tests |
| `ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs` | Add 4 Reactivate tests; add 4 EstimatedAmount nullability tests; add Dismiss-with-body tests |
| `ProjectCeres.Tests/Integration/Api/UiVerificationTests.cs` | Delete recurring Razor view smoke test entries |

### Delete — server

- `ProjectCeres/Views/RecurringTransactions/Index.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Create.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Edit.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Confirm.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Dismiss.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Deactivate.cshtml`
- `ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml`
- `ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml`
- `ProjectCeres/ViewModels/RecurringTransactionCreateViewModel.cs`
- `ProjectCeres/ViewModels/RecurringTransactionEditViewModel.cs`

### Create — client

| Path | Purpose |
|---|---|
| `ProjectCeres.Client/src/app/features/recurring/recurring-api.ts` | URL builders + DTOs (logic-free) |
| `ProjectCeres.Client/src/app/features/recurring/reminder-status.ts` | Pure helpers: `classifyStatus` → `ReminderStatus[]` |
| `ProjectCeres.Client/src/app/features/recurring/reminder-status.test.ts` | Tests for `classifyStatus` |
| `ProjectCeres.Client/src/app/features/recurring/RecurringTable.tsx` | Pure UI table (6 col + ⋯) |
| `ProjectCeres.Client/src/app/features/recurring/RecurringTable.test.tsx` | Table rendering tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.tsx` | AlertDialog body for Confirm flow |
| `ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.test.tsx` | Confirm dialog tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.tsx` | AlertDialog body for Dismiss flow |
| `ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.test.tsx` | Dismiss dialog tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx` | ⋯ menu + all dialogs |
| `ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.test.tsx` | Row menu tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringForm.tsx` | Shared Create/Edit form, conditional fields |
| `ProjectCeres.Client/src/app/features/recurring/RecurringForm.test.tsx` | Form tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringCreate.tsx` | Page glue for /new |
| `ProjectCeres.Client/src/app/features/recurring/RecurringCreate.test.tsx` | Create page tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringEdit.tsx` | Page glue for /:id/edit |
| `ProjectCeres.Client/src/app/features/recurring/RecurringEdit.test.tsx` | Edit page tests |
| `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx` | List page glue + Outlet host |
| `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx` | Layout tests |
| `ProjectCeres.Client/src/app/layout/ReminderCountProvider.tsx` | Context for bell badge count + `refresh()` |
| `ProjectCeres.Client/src/app/layout/ReminderCountProvider.test.tsx` | Provider tests |

### Modify — client

| Path | Changes |
|---|---|
| `ProjectCeres.Client/src/app/pages/Recurring.tsx` | One-line re-export of `RecurringLayout` |
| `ProjectCeres.Client/src/app/App.tsx` | Nest `recurring` with `/new` and `/:id/edit` children; wrap shell in `<ReminderCountProvider>` |
| `ProjectCeres.Client/src/app/layout/TopBar.tsx` | Rewrite `NotificationsButton` to use context, badge count, popover list |
| `ProjectCeres.Client/src/app/layout/TopBar.test.tsx` | Add 6 new tests for bell/popover behaviour |
| `ProjectCeres.Client/src/app/App.test.tsx` | Update recurring smoke test to expect live page |

---

## Tasks 1–10 — Server side

---

### Task 1: `RecurringTransactionPolicies` — new static class

**Files:**
- Create: `ProjectCeres/Services/RecurringTransactionPolicies.cs`
- Create: `ProjectCeres.Tests/Integration/RecurringTransactionPoliciesTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// ProjectCeres.Tests/Integration/RecurringTransactionPoliciesTests.cs
using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

public class RecurringTransactionPoliciesTests
{
    [Fact]
    public void ValidateSchedule_SnapMonthly_WithDay15_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapMonthly_WithDay32_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 32);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapWeekly_WithDay4_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 4);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapWeekly_WithDay8_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 8);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_Manual_WithDay15_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.ManualDate, dayOfPeriod: 15);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapAnnual_WithDay15_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Annual, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapMonthly_WithNullDay_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: null);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_Manual_WithNullDay_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.ManualDate, dayOfPeriod: null);
        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd <repo>
dotnet test --filter "RecurringTransactionPoliciesTests"
```

Expected: compilation error — `RecurringTransactionPolicies` not found.

- [ ] **Step 3: Create the policies class**

```csharp
// ProjectCeres/Services/RecurringTransactionPolicies.cs
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public static class RecurringTransactionPolicies
{
    public const string InvalidDayOfPeriodCode = "INVALID_DAY_OF_PERIOD";

    public static Result ValidateSchedule(Frequency frequency, ReminderBehaviour behaviour, int? dayOfPeriod)
    {
        var snapNonAnnual = behaviour == ReminderBehaviour.SnapToCalendarDay
                         && frequency != Frequency.Annual;

        if (!snapNonAnnual && dayOfPeriod is not null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period applies only to Snap-to-calendar-day reminders that are not Annual.");

        if (snapNonAnnual && dayOfPeriod is null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period is required for Snap-to-calendar-day reminders.");

        if (snapNonAnnual)
        {
            var max = frequency == Frequency.Monthly ? 31 : 7;
            if (dayOfPeriod < 1 || dayOfPeriod > max)
                return Result.Fail(InvalidDayOfPeriodCode,
                    $"Day of period must be 1–{max} for {frequency} reminders.");
        }

        return Result.Ok();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "RecurringTransactionPoliciesTests"
```

Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Services/RecurringTransactionPolicies.cs \
        ProjectCeres.Tests/Integration/RecurringTransactionPoliciesTests.cs
git commit -m "feat(recurring): add RecurringTransactionPolicies.ValidateSchedule"
```

---

### Task 2: Fix `SnapToCalendarDay` for Weekly + Biweekly frequencies

**Files:**
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs:147–168`
- Modify: `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs` (add tests)

- [ ] **Step 1: Write the failing Snap tests**

Open `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs` and append these tests inside the existing test class. (Find the class name by grepping: `grep "class Recurring" ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs`.)

```csharp
// Snap-to-weekday tests — append to the existing test class
[Fact]
public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromEarlierInWeek()
{
    // Confirm on Wednesday 2026-04-29 (Wed), target Monday (DayOfPeriod=1)
    var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
        nextDueDate: new DateOnly(2026, 4, 27)); // arbitrary
    var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 29)); // Wed
    // Next Monday from Wed: 2026-05-04
    result.Should().Be(new DateOnly(2026, 5, 4));
}

[Fact]
public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromTargetDay()
{
    // Confirm on Monday 2026-04-28 (Mon), target Monday (DayOfPeriod=1)
    var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
        nextDueDate: new DateOnly(2026, 4, 28));
    var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 28));
    // Same-day → next Monday: 2026-05-04 (7 days later, since daysAhead=0 → set to 7)
    result.Should().Be(new DateOnly(2026, 5, 4));
}

[Fact]
public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromLaterInWeek()
{
    // Confirm on Friday 2026-05-01, target Wednesday (DayOfPeriod=3)
    var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
        nextDueDate: new DateOnly(2026, 5, 1));
    var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 1)); // Fri
    // Next Wednesday from Fri: 2026-05-06
    result.Should().Be(new DateOnly(2026, 5, 6));
}

[Fact]
public void SnapToCalendarDay_Biweekly_AdvancesAtLeast8Days_FromTargetDay()
{
    // Confirm on Wednesday 2026-04-29, target Wednesday (DayOfPeriod=3)
    var reminder = MakeReminder(Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
        nextDueDate: new DateOnly(2026, 4, 29));
    var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 29));
    // daysAhead=0 → 7; then biweekly: +7 more → 14 days → 2026-05-13
    result.Should().Be(new DateOnly(2026, 5, 13));
}

[Fact]
public void SnapToCalendarDay_Biweekly_TargetMidweek()
{
    // Confirm on Monday 2026-04-28, target Wednesday (DayOfPeriod=3)
    var reminder = MakeReminder(Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
        nextDueDate: new DateOnly(2026, 4, 28));
    var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 28));
    // Next Wed from Mon: 2 days (daysAhead=2 < 8 → add 7) → 9 days → 2026-05-07
    result.Should().Be(new DateOnly(2026, 5, 7));
}
```

You also need two private helpers in the test class (or a base class, following the existing pattern):

```csharp
// Check the existing test file for how it creates reminder objects and invokes private methods.
// If it uses a test helper method already, follow that shape. If not, use reflection:

private static RecurringTransaction MakeReminder(
    Frequency frequency, ReminderBehaviour behaviour, int? dayOfPeriod, DateOnly nextDueDate)
{
    return new RecurringTransaction
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(),
        Name = "Test", AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(),
        Frequency = frequency, ReminderBehaviour = behaviour,
        DayOfPeriod = dayOfPeriod, NextDueDate = nextDueDate, IsActive = true,
    };
}

private static DateOnly InvokeSnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
{
    // SnapToCalendarDay is private static — invoke via reflection
    var method = typeof(RecurringTransactionService).GetMethod(
        "SnapToCalendarDay",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    return (DateOnly)method.Invoke(null, [reminder, confirmDate])!;
}
```

> **Note:** Before writing these helpers, check if `RecurringTransactionServiceTests.cs` already has equivalent helpers. If it does, use those instead.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "SnapToCalendarDay_Weekly|SnapToCalendarDay_Biweekly"
```

Expected: FAILs — the current `SnapToCalendarDay` ignores DayOfPeriod for non-Monthly frequencies.

- [ ] **Step 3: Rewrite `SnapToCalendarDay` and add `SnapWeekly` helper**

Replace `RecurringTransactionService.cs:147–168`:

```csharp
private static DateOnly SnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
{
    if (reminder.DayOfPeriod is null)
        return AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency);

    return reminder.Frequency switch
    {
        Frequency.Monthly  => SnapMonthly(reminder, confirmDate),
        Frequency.Weekly   => SnapWeekly(reminder, confirmDate, doubleStep: false),
        Frequency.Biweekly => SnapWeekly(reminder, confirmDate, doubleStep: true),
        _                  => AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency),
    };
}

private static DateOnly SnapMonthly(RecurringTransaction reminder, DateOnly? confirmDate)
{
    var day  = reminder.DayOfPeriod!.Value;
    var from = confirmDate ?? reminder.NextDueDate;

    var candidate = new DateOnly(from.Year, from.Month, 1).AddMonths(1);
    var daysInMonth = DateTime.DaysInMonth(candidate.Year, candidate.Month);
    candidate = new DateOnly(candidate.Year, candidate.Month, Math.Min(day, daysInMonth));

    if (from.Day >= day)
        candidate = candidate.AddMonths(1);

    return candidate;
}

private static DateOnly SnapWeekly(RecurringTransaction reminder, DateOnly? confirmDate, bool doubleStep)
{
    // DayOfPeriod: 1=Monday … 7=Sunday (ISO 8601)
    // DayOfWeek:   Sunday=0 … Saturday=6
    var targetDow = (DayOfWeek)(reminder.DayOfPeriod!.Value % 7);
    var from = confirmDate ?? reminder.NextDueDate;
    var daysAhead = ((int)targetDow - (int)from.DayOfWeek + 7) % 7;
    if (daysAhead == 0) daysAhead = 7;           // never return same-day
    if (doubleStep && daysAhead < 8) daysAhead += 7;
    return from.AddDays(daysAhead);
}
```

Remove the old `SnapToCalendarDay` body (the entire old method is replaced — the new version calls `SnapMonthly` which contains the clamp logic that was previously inline).

- [ ] **Step 4: Run Snap tests to verify they pass**

```bash
dotnet test --filter "SnapToCalendarDay"
```

Expected: all Snap tests pass.

- [ ] **Step 5: Run full server test suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/RecurringTransactionService.cs \
        ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs
git commit -m "fix(recurring): honour DayOfPeriod for Weekly+Biweekly Snap-to-calendar-day"
```

---

### Task 3: EstimatedAmount nullability cleanup + `DismissRecurringTransactionRequest` DTO

**Files:**
- Modify: `ProjectCeres/ViewModels/RecurringTransactionApiDtos.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs` (add tests)

- [ ] **Step 1: Write failing tests for EstimatedAmount round-trip**

Append to `RecurringTransactionsCrudApiTests.cs` (inside existing test class):

```csharp
[Fact]
public async Task Post_with_null_estimated_amount_persists_null()
{
    var req = ValidCreateRequest() with { EstimatedAmount = null };
    var post = await Client.PostAsJsonAsync("/api/recurring-transactions", req);
    post.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await post.Content.ReadFromJsonAsync<RecurringTransactionDetailDto>())!.Id;

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.EstimatedAmount.Should().BeNull();
}

[Fact]
public async Task Post_with_zero_estimated_amount_persists_zero()
{
    var req = ValidCreateRequest() with { EstimatedAmount = 0m };
    var post = await Client.PostAsJsonAsync("/api/recurring-transactions", req);
    post.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await post.Content.ReadFromJsonAsync<RecurringTransactionDetailDto>())!.Id;

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.EstimatedAmount.Should().Be(0m);
}

[Fact]
public async Task Patch_can_set_estimated_amount_to_null()
{
    var id = await CreateReminderWithAmount(100m);
    var req = ValidUpdateRequest() with { EstimatedAmount = null };
    var patch = await Client.PatchAsJsonAsync($"/api/recurring-transactions/{id}", req);
    patch.StatusCode.Should().Be(HttpStatusCode.OK);

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.EstimatedAmount.Should().BeNull();
}

[Fact]
public async Task Patch_can_set_estimated_amount_to_value()
{
    var id = await CreateReminderWithAmount(null);
    var req = ValidUpdateRequest() with { EstimatedAmount = 12.99m };
    var patch = await Client.PatchAsJsonAsync($"/api/recurring-transactions/{id}", req);
    patch.StatusCode.Should().Be(HttpStatusCode.OK);

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.EstimatedAmount.Should().Be(12.99m);
}
```

> **Note:** Check the existing test file for how `ValidCreateRequest()`, `ValidUpdateRequest()`, and `CreateReminderWithAmount()` are defined. If they don't exist, add them following the established helper pattern in that file. A `ValidCreateRequest` returns a fully valid `CreateRecurringTransactionRequest`-shaped object (or anonymous type). `CreateReminderWithAmount` posts a reminder and returns its `Guid`.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "Post_with_null_estimated_amount|Patch_can_set_estimated_amount"
```

Expected: FAIL — the current DTO rejects `null` because `EstimatedAmount` is `decimal` (non-nullable).

- [ ] **Step 3: Update the DTOs**

In `ProjectCeres/ViewModels/RecurringTransactionApiDtos.cs`, change `CreateRecurringTransactionRequest` and `UpdateRecurringTransactionRequest`:

```csharp
public record CreateRecurringTransactionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 999_999_999_999.99)] decimal? EstimatedAmount,
    [Required] Guid? AccountId,
    [Required] Guid? CategoryId,
    [Required] string Frequency,
    [Range(1, 31)] int? DayOfPeriod,
    [Required] DateOnly NextDueDate,
    string ReminderBehaviour);

public record UpdateRecurringTransactionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 999_999_999_999.99)] decimal? EstimatedAmount,
    [Required] Guid? AccountId,
    [Required] Guid? CategoryId,
    [Required] string Frequency,
    [Range(1, 31)] int? DayOfPeriod,
    [Required] DateOnly NextDueDate,
    string ReminderBehaviour);
```

Also add the new Dismiss DTO at the end of the file:

```csharp
public record DismissRecurringTransactionRequest(DateOnly? NextDueDate);
```

- [ ] **Step 4: Run the new tests**

```bash
dotnet test --filter "Post_with_null_estimated_amount|Post_with_zero_estimated_amount|Patch_can_set_estimated_amount"
```

Expected: all pass.

- [ ] **Step 5: Run full server suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/ViewModels/RecurringTransactionApiDtos.cs \
        ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs
git commit -m "fix(recurring): make EstimatedAmount nullable on Create/Update DTOs; add DismissRequest DTO"
```

---

### Task 4: Wire `RecurringTransactionPolicies` into `TryCreateAsync` / `TryUpdateAsync`

**Files:**
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs`

- [ ] **Step 1: Write a failing test that verifies policy enforcement on Create**

Append to `RecurringTransactionsCrudApiTests.cs`:

```csharp
[Fact]
public async Task Post_with_invalid_day_of_period_returns_422()
{
    // Weekly + Snap + dayOfPeriod=8 (out of range 1–7)
    var req = ValidCreateRequest() with
    {
        Frequency = "Weekly",
        ReminderBehaviour = "SnapToCalendarDay",
        DayOfPeriod = 8
    };
    var post = await Client.PostAsJsonAsync("/api/recurring-transactions", req);
    post.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await post.Content.ReadAsStringAsync();
    body.Should().Contain("INVALID_DAY_OF_PERIOD");
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test --filter "Post_with_invalid_day_of_period_returns_422"
```

Expected: FAIL — currently no policy check is called.

- [ ] **Step 3: Add policy call in `TryCreateAsync` and `TryUpdateAsync`**

In `RecurringTransactionService.cs`, after the enum parses for `freq` and `behaviour` in `TryCreateAsync`, add:

```csharp
var scheduleCheck = RecurringTransactionPolicies.ValidateSchedule(freq, behaviour, request.DayOfPeriod);
if (!scheduleCheck.IsSuccess)
    return Result<RecurringTransaction>.Fail(scheduleCheck.Error!.Value.Code, scheduleCheck.Error!.Value.Message);
```

Do the same in `TryUpdateAsync` after its enum parses.

- [ ] **Step 4: Run the new test**

```bash
dotnet test --filter "Post_with_invalid_day_of_period_returns_422"
```

Expected: PASS.

- [ ] **Step 5: Run full server suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Services/RecurringTransactionService.cs \
        ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs
git commit -m "feat(recurring): enforce DayOfPeriod policy in TryCreateAsync and TryUpdateAsync"
```

---

### Task 5: Add `TryReactivateAsync` — service + interface + controller

**Files:**
- Modify: `ProjectCeres/Services/IRecurringTransactionService.cs`
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs`
- Modify: `ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs`
- Modify: `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs`

- [ ] **Step 1: Write failing Reactivate API tests**

Append to `RecurringTransactionsCrudApiTests.cs`:

```csharp
[Fact]
public async Task Reactivate_returns_204_for_archived_reminder()
{
    var id = await CreateAndArchiveReminder();
    var patch = await Client.PatchAsync($"/api/recurring-transactions/{id}/reactivate", null);
    patch.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.IsActive.Should().BeTrue();
}

[Fact]
public async Task Reactivate_returns_204_for_already_active_reminder()
{
    var id = await CreateReminderAndReturnId();
    var patch = await Client.PatchAsync($"/api/recurring-transactions/{id}/reactivate", null);
    patch.StatusCode.Should().Be(HttpStatusCode.NoContent);
}

[Fact]
public async Task Reactivate_returns_404_for_unknown_id()
{
    var patch = await Client.PatchAsync($"/api/recurring-transactions/{Guid.NewGuid()}/reactivate", null);
    patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task Reactivate_returns_404_for_intruder_row()
{
    var otherId = await CreateReminderAsOtherUser();
    var patch = await Client.PatchAsync($"/api/recurring-transactions/{otherId}/reactivate", null);
    patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

> **Note:** Check the existing test file for `CreateAndArchiveReminder()`, `CreateReminderAndReturnId()`, and `CreateReminderAsOtherUser()` helpers. Add them if missing, following the established pattern (e.g. POST to create, then PATCH to archive).

- [ ] **Step 2: Write failing service-layer Reactivate tests**

Append to `RecurringTransactionServiceTests.cs`:

```csharp
[Fact]
public async Task TryReactivateAsync_returns_ok_for_archived_reminder()
{
    var id = await CreateAndArchiveReminderInDb();
    var result = await Service.TryReactivateAsync(id);
    result.IsSuccess.Should().BeTrue();
    var reminder = await Db.RecurringTransactions.FindAsync(id);
    reminder!.IsActive.Should().BeTrue();
}

[Fact]
public async Task TryReactivateAsync_is_idempotent_for_active_reminder()
{
    var id = await CreateActiveReminderInDb();
    var result = await Service.TryReactivateAsync(id);
    result.IsSuccess.Should().BeTrue();
}

[Fact]
public async Task TryReactivateAsync_returns_fail_for_unknown_id()
{
    var result = await Service.TryReactivateAsync(Guid.NewGuid());
    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Code.Should().Be("NOT_FOUND");
}
```

> **Note:** Check the existing test file for how `Service`, `Db`, `CreateActiveReminderInDb()`, etc. are surfaced. Follow the same pattern.

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter "Reactivate|TryReactivateAsync"
```

Expected: compilation error — `TryReactivateAsync` not defined.

- [ ] **Step 4: Add `TryReactivateAsync` to the interface**

In `IRecurringTransactionService.cs`, add under the API surface block:

```csharp
Task<Result> TryReactivateAsync(Guid id);
```

- [ ] **Step 5: Implement `TryReactivateAsync` in the service**

Append to `RecurringTransactionService.cs` after `TryDismissAsync`:

```csharp
public async Task<Result> TryReactivateAsync(Guid id)
{
    var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
    if (reminder is null) return Result.Fail("NOT_FOUND", "Recurring transaction not found.");
    if (reminder.IsActive) return Result.Ok();
    reminder.IsActive = true;
    await db.SaveChangesAsync();
    return Result.Ok();
}
```

- [ ] **Step 6: Add `Reactivate` action in the API controller**

In `RecurringTransactionsApiController.cs`, after the `Archive` action:

```csharp
[HttpPatch("{id:guid}/reactivate")]
public async Task<IActionResult> Reactivate(Guid id)
{
    var result = await reminderService.TryReactivateAsync(id);
    return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
}
```

- [ ] **Step 7: Run Reactivate tests**

```bash
dotnet test --filter "Reactivate|TryReactivateAsync"
```

Expected: all pass.

- [ ] **Step 8: Run full server suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 9: Commit**

```bash
git add ProjectCeres/Services/IRecurringTransactionService.cs \
        ProjectCeres/Services/RecurringTransactionService.cs \
        ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs \
        ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs \
        ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs
git commit -m "feat(recurring): add TryReactivateAsync + PATCH /reactivate endpoint"
```

---

### Task 6: Update `Dismiss` to accept an optional body (`nextDueDate` for ManualDate)

**Files:**
- Modify: `ProjectCeres/Services/IRecurringTransactionService.cs`
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs`
- Modify: `ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs`

> **Context:** The current `TryDismissAsync(Guid)` accepts no nextDueDate; the SPA needs to send one for ManualDate reminders. The `DismissRecurringTransactionRequest` DTO was added in Task 3.

- [ ] **Step 1: Write failing tests for Dismiss-with-body**

Append to `RecurringTransactionsCrudApiTests.cs`:

```csharp
[Fact]
public async Task Dismiss_ManualDate_with_next_due_date_advances_schedule()
{
    var id = await CreateManualDateReminder(nextDueDate: new DateOnly(2026, 5, 3));
    var body = new { nextDueDate = "2026-06-01" };
    var res = await Client.PostAsJsonAsync($"/api/recurring-transactions/{id}/dismiss", body);
    res.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.NextDueDate.Should().Be(new DateOnly(2026, 6, 1));
}

[Fact]
public async Task Dismiss_SnapMonthly_without_body_advances_schedule()
{
    var id = await CreateSnapMonthlyReminder(nextDueDate: new DateOnly(2026, 5, 15), dayOfPeriod: 15);
    var res = await Client.PostAsync($"/api/recurring-transactions/{id}/dismiss", null);
    res.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var get = await Client.GetFromJsonAsync<RecurringTransactionDetailDto>($"/api/recurring-transactions/{id}");
    get!.NextDueDate.Should().Be(new DateOnly(2026, 6, 15));
}
```

> **Note:** Add `CreateManualDateReminder` and `CreateSnapMonthlyReminder` helpers if not present.

- [ ] **Step 2: Run to verify the ManualDate test fails**

```bash
dotnet test --filter "Dismiss_ManualDate|Dismiss_SnapMonthly"
```

Expected: ManualDate test fails (nextDueDate ignored). SnapMonthly may pass already.

- [ ] **Step 3: Update the interface signature**

In `IRecurringTransactionService.cs`, change:

```csharp
Task<Result> TryDismissAsync(Guid id);
```

to:

```csharp
Task<Result> TryDismissAsync(Guid id, DateOnly? nextDueDate);
```

- [ ] **Step 4: Update the service implementation**

In `RecurringTransactionService.cs`, change `TryDismissAsync`:

```csharp
public async Task<Result> TryDismissAsync(Guid id, DateOnly? nextDueDate)
{
    var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
    if (reminder is null) return Result.Fail("NOT_FOUND", "Recurring transaction not found.");
    reminder.NextDueDate = AdvanceDueDate(reminder, confirmDate: null, nextDueDate: nextDueDate);
    await db.SaveChangesAsync();
    return Result.Ok();
}
```

- [ ] **Step 5: Update the controller action**

Replace the `Dismiss` action in `RecurringTransactionsApiController.cs`:

```csharp
[HttpPost("{id:guid}/dismiss")]
public async Task<IActionResult> Dismiss(Guid id, [FromBody] DismissRecurringTransactionRequest? body)
{
    var result = await reminderService.TryDismissAsync(id, body?.NextDueDate);
    return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
}
```

- [ ] **Step 6: Run Dismiss tests**

```bash
dotnet test --filter "Dismiss"
```

Expected: all pass.

- [ ] **Step 7: Run full server suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Services/IRecurringTransactionService.cs \
        ProjectCeres/Services/RecurringTransactionService.cs \
        ProjectCeres/Controllers/Api/RecurringTransactionsApiController.cs \
        ProjectCeres.Tests/Integration/Api/RecurringTransactionsCrudApiTests.cs
git commit -m "feat(recurring): Dismiss accepts optional nextDueDate body for ManualDate reminders"
```

---

### Task 7: Drop throwing Razor-era methods + slim `RecurringTransactionsController` to redirects

**Files:**
- Modify: `ProjectCeres/Services/IRecurringTransactionService.cs`
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs`
- Modify: `ProjectCeres/Controllers/RecurringTransactionsController.cs`
- Modify: `ProjectCeres.Tests/Integration/RecurringTransactionServiceTests.cs`
- Modify: `ProjectCeres.Tests/Integration/Api/UiVerificationTests.cs`
- Delete: 8 Razor view files + 2 ViewModel files

- [ ] **Step 1: Remove throwing method declarations from the interface**

In `IRecurringTransactionService.cs`, remove these lines:

```csharp
Task<RecurringTransaction> CreateAsync(RecurringTransactionCreateViewModel vm);
Task UpdateAsync(RecurringTransactionEditViewModel vm);
Task DeactivateAsync(Guid id);
Task<Transaction> ConfirmAsync(Guid id, DateOnly date, decimal amount, string? description, DateOnly? nextDueDate = null);
Task DismissAsync(Guid id);
```

Also remove the `// Razor-era methods (throwing). Used by RecurringTransactionsController.` comment.

- [ ] **Step 2: Remove the throwing method bodies from the service**

In `RecurringTransactionService.cs`, delete:
- `CreateAsync(RecurringTransactionCreateViewModel vm)` method body
- `UpdateAsync(RecurringTransactionEditViewModel vm)` method body
- `DeactivateAsync(Guid id)` method body (the throwing one)
- `ConfirmAsync(Guid id, DateOnly date, decimal amount, string? description, DateOnly? nextDueDate = null)` method body
- `DismissAsync(Guid id)` method body (the throwing one, signature `Task DismissAsync(Guid id)`)

Also remove the `using ProjectCeres.ViewModels;` import if it's no longer needed (it may still be needed for the request DTOs — check).

- [ ] **Step 3: Slim `RecurringTransactionsController` to redirects**

Replace the entire controller body (keep the class shell):

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ProjectCeres.Controllers;

public class RecurringTransactionsController : Controller
{
    public IActionResult Index()             => Redirect("/app/recurring");
    public IActionResult Create()            => Redirect("/app/recurring/new");
    public IActionResult Edit(Guid id)       => Redirect($"/app/recurring/{id}/edit");
    public IActionResult Confirm(Guid id)    => Redirect("/app/recurring");
    public IActionResult Dismiss(Guid id)    => Redirect("/app/recurring");
    public IActionResult Deactivate(Guid id) => Redirect("/app/recurring");
    public IActionResult Upcoming()          => Redirect("/app/recurring");
}
```

- [ ] **Step 4: Delete Razor views**

```bash
rm ProjectCeres/Views/RecurringTransactions/Index.cshtml
rm ProjectCeres/Views/RecurringTransactions/Create.cshtml
rm ProjectCeres/Views/RecurringTransactions/Edit.cshtml
rm ProjectCeres/Views/RecurringTransactions/Confirm.cshtml
rm ProjectCeres/Views/RecurringTransactions/Dismiss.cshtml
rm ProjectCeres/Views/RecurringTransactions/Deactivate.cshtml
rm ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml
rm ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml
```

- [ ] **Step 5: Delete Razor-era ViewModels**

```bash
rm ProjectCeres/ViewModels/RecurringTransactionCreateViewModel.cs
rm ProjectCeres/ViewModels/RecurringTransactionEditViewModel.cs
```

- [ ] **Step 6: Drop tests for deleted throwing methods**

In `RecurringTransactionServiceTests.cs`, delete any test methods that reference `CreateAsync(vm)`, `UpdateAsync(vm)`, `DeactivateAsync`, `ConfirmAsync`, or `DismissAsync` (the throwing variants). Keep all the `TryCreateAsync`, `TryUpdateAsync`, `TryDeactivateAsync`, `TryConfirmAsync`, `TryDismissAsync`, Snap, and Reactivate tests.

In `UiVerificationTests.cs`, find and delete the test entries for Recurring Razor view smoke tests (lines 178, 206, 221 per the spec — verify exact line numbers by opening the file and searching for "RecurringTransactions").

- [ ] **Step 7: Run full server suite**

```bash
dotnet test
```

Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add -u   # picks up all modifications and deletions
git commit -m "refactor(recurring): drop Razor-era service methods; slim controller to 302 redirects; delete views+viewmodels"
```

---

### Task 8: Fix `docs/models.md` drift items

**Files:**
- Modify: `docs/models.md`

> **Before writing:** open `docs/models.md` and verify current content around Dismiss, EstimatedAmount, and Snap behaviour. Fix only the drifted lines — do not rewrite surrounding content.

- [ ] **Step 1: Open and read the relevant sections**

```bash
grep -n "Dismiss\|EstimatedAmount\|DayOfPeriod\|Snap" docs/models.md
```

- [ ] **Step 2: Apply three targeted fixes**

Fix 1 — Dismiss description: wherever the doc says Dismiss does not advance NextDueDate (or is silent on it), update to say: "Dismiss advances NextDueDate per the ReminderBehaviour without creating a transaction."

Fix 2 — EstimatedAmount nullability: wherever the doc describes EstimatedAmount, clarify: "`EstimatedAmount: decimal?` — `null` means the amount varies per occurrence. `0` is a valid stored amount."

Fix 3 — Weekly/Biweekly Snap: add a note in the DayOfPeriod row (or wherever Frequency/DayOfPeriod semantics are described): "For Weekly and Biweekly Snap reminders, `DayOfPeriod` is a day-of-week (1=Monday … 7=Sunday, ISO 8601). For Monthly Snap, it is a day-of-month (1–31). For Annual and non-Snap reminders, DayOfPeriod must be null."

- [ ] **Step 3: Commit**

```bash
git add docs/models.md
git commit -m "docs(models): fix Dismiss/EstimatedAmount/Snap drift"
```

---

### Task 9: Smoke-test server side end-to-end

> No new files. Verify the running server serves reminders correctly before starting the React work.

- [ ] **Step 1: Start the .NET server**

```bash
dotnet run --project ProjectCeres
```

- [ ] **Step 2: Verify key API endpoints**

```bash
# List reminders
curl -s http://localhost:5117/api/recurring-transactions | jq .

# Upcoming (bell data source)
curl -s "http://localhost:5117/api/recurring-transactions/upcoming?days=0" | jq .

# Reactivate an archived reminder (substitute a real archived ID)
# curl -s -X PATCH http://localhost:5117/api/recurring-transactions/<id>/reactivate -w "\n%{http_code}"
```

- [ ] **Step 3: Visit Razor redirect**

Navigate to `/RecurringTransactions` in a browser. Expect a 302 to `/app/recurring`.

- [ ] **Step 4: Stop the server** (Ctrl-C)

---

### Task 10: Write the spec doc (per spec §1 requirement)

**Files:**
- Create: `docs/superpowers/specs/2026-05-03-recurring-spa-design.md`

> The brainstorm file at `~/.claude/plans/brainstorm-the-recurring-transactions-scalable-cray.md` is the canonical spec content. Copy it verbatim to the docs location so it's committed alongside the implementation.

- [ ] **Step 1: Copy the brainstorm to the spec path**

```bash
cp ~/.claude/plans/brainstorm-the-recurring-transactions-scalable-cray.md \
   <repo>/docs/superpowers/specs/2026-05-03-recurring-spa-design.md
```

Update the `> **Status:**` line at the top to read:

```
> **Status:** Spec locked 2026-05-03. Committed alongside implementation.
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/specs/2026-05-03-recurring-spa-design.md
git commit -m "docs(recurring): commit locked spec for SPA migration"
```

---

## Tasks 11–20 — Client side + TopBar + Razor cutover + docs sync

---

### Task 11: `recurring-api.ts` + `reminder-status.ts` — URL builders, DTOs, status helpers

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/recurring-api.ts`
- Create: `ProjectCeres.Client/src/app/features/recurring/reminder-status.ts`
- Create: `ProjectCeres.Client/src/app/features/recurring/reminder-status.test.ts`

- [ ] **Step 1: Write failing `classifyStatus` tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/reminder-status.test.ts
import { describe, it, expect } from 'vitest';
import { classifyStatus, type RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: 'abc', name: 'Test', estimatedAmount: 100, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Bills', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    ...overrides,
  };
}

describe('classifyStatus', () => {
  it('archived reminder returns [archived] only', () => {
    expect(classifyStatus(makeReminder({ isActive: false, nextDueDate: '2026-04-01' }), TODAY))
      .toEqual(['archived']);
  });

  it('overdue returns [overdue]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-04-30' }), TODAY))
      .toEqual(['overdue']);
  });

  it('due today returns [dueToday]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: TODAY }), TODAY))
      .toEqual(['dueToday']);
  });

  it('upcoming returns []', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-05-10' }), TODAY))
      .toEqual([]);
  });

  it('overdue ManualDate returns [overdue, manual]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-04-01', reminderBehaviour: 'ManualDate' }), TODAY))
      .toEqual(['overdue', 'manual']);
  });

  it('upcoming ManualDate returns [manual]', () => {
    expect(classifyStatus(makeReminder({ nextDueDate: '2026-06-01', reminderBehaviour: 'ManualDate' }), TODAY))
      .toEqual(['manual']);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd <repo>/ProjectCeres.Client
pnpm test reminder-status
```

Expected: module not found.

- [ ] **Step 3: Create `reminder-status.ts`**

```typescript
// ProjectCeres.Client/src/app/features/recurring/reminder-status.ts
export type ReminderStatus = 'overdue' | 'dueToday' | 'upcoming' | 'archived' | 'manual';

export type RecurringTransactionListItemDto = {
  id: string;
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  accountName: string;
  currencySymbol: string;
  categoryId: string;
  categoryName: string;
  categoryTypeName: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;       // ISO date string 'YYYY-MM-DD'
  isActive: boolean;
  reminderBehaviour: string;
};

export function classifyStatus(
  reminder: RecurringTransactionListItemDto,
  today: string
): ReminderStatus[] {
  if (!reminder.isActive) return ['archived'];
  const out: ReminderStatus[] = [];
  if (reminder.nextDueDate < today) out.push('overdue');
  else if (reminder.nextDueDate === today) out.push('dueToday');
  if (reminder.reminderBehaviour === 'ManualDate') out.push('manual');
  return out;
}
```

- [ ] **Step 4: Create `recurring-api.ts`**

```typescript
// ProjectCeres.Client/src/app/features/recurring/recurring-api.ts
export type { RecurringTransactionListItemDto } from './reminder-status';

export const RECURRING_URL = '/api/recurring-transactions';
export const RECURRING_BY_ID_URL = (id: string) => `/api/recurring-transactions/${id}`;
export const RECURRING_ARCHIVE_URL = (id: string) => `/api/recurring-transactions/${id}/archive`;
export const RECURRING_REACTIVATE_URL = (id: string) => `/api/recurring-transactions/${id}/reactivate`;
export const RECURRING_CONFIRM_URL = (id: string) => `/api/recurring-transactions/${id}/confirm`;
export const RECURRING_DISMISS_URL = (id: string) => `/api/recurring-transactions/${id}/dismiss`;
export const RECURRING_UPCOMING_URL = (days: number) =>
  `/api/recurring-transactions/upcoming?days=${days}`;

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${RECURRING_URL}?includeInactive=true` : RECURRING_URL;
}

export type RecurringTransactionDetailDto = {
  id: string;
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  categoryId: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
  isActive: boolean;
  reminderBehaviour: string;
};

export type CreateRecurringTransactionRequest = {
  name: string;
  estimatedAmount: number | null;
  accountId: string;
  categoryId: string;
  frequency: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
  reminderBehaviour: string;
};

export type UpdateRecurringTransactionRequest = CreateRecurringTransactionRequest;

export type ConfirmRecurringTransactionRequest = {
  date: string;
  amount: number;
  description: string | null;
  nextDueDate: string | null;
};

export type DismissRecurringTransactionRequest = {
  nextDueDate: string | null;
};
```

- [ ] **Step 5: Run `classifyStatus` tests**

```bash
pnpm test reminder-status
```

Expected: all 6 pass.

- [ ] **Step 6: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/recurring-api.ts \
        ProjectCeres.Client/src/app/features/recurring/reminder-status.ts \
        ProjectCeres.Client/src/app/features/recurring/reminder-status.test.ts
git commit -m "feat(recurring/client): add recurring-api.ts, reminder-status.ts + tests"
```

---

### Task 12: `RecurringTable` — pure UI table component

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringTable.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringTable.test.tsx`

- [ ] **Step 1: Write failing tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringTable.test.tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { RecurringTable } from './RecurringTable';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeRow(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900,
    accountId: 'a', accountName: 'Checking', currencySymbol: '€',
    categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-04-28',
    isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    ...overrides,
  };
}

const noop = vi.fn();

function renderTable(rows: RecurringTransactionListItemDto[]) {
  return render(
    <MemoryRouter>
      <RecurringTable rows={rows} today={TODAY} onChanged={noop} />
    </MemoryRouter>
  );
}

describe('RecurringTable', () => {
  it('renders a row with visible data columns', () => {
    renderTable([makeRow()]);
    expect(screen.getByText('Rent')).toBeInTheDocument();
    expect(screen.getByText('Checking')).toBeInTheDocument();
    expect(screen.getByText('Housing')).toBeInTheDocument();
    expect(screen.getByText('Monthly')).toBeInTheDocument();
    expect(screen.getByText('€900')).toBeInTheDocument();
  });

  it('renders em-dash when estimatedAmount is null', () => {
    renderTable([makeRow({ estimatedAmount: null })]);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows Overdue badge for past nextDueDate', () => {
    renderTable([makeRow({ nextDueDate: '2026-04-01' })]);
    expect(screen.getByText('Overdue')).toBeInTheDocument();
  });

  it('shows Due today badge for today nextDueDate', () => {
    renderTable([makeRow({ nextDueDate: TODAY })]);
    expect(screen.getByText('Due today')).toBeInTheDocument();
  });

  it('shows Archived badge for inactive row', () => {
    renderTable([makeRow({ isActive: false })]);
    expect(screen.getByText('Archived')).toBeInTheDocument();
  });

  it('shows Manual badge for ManualDate behaviour', () => {
    renderTable([makeRow({ reminderBehaviour: 'ManualDate' })]);
    expect(screen.getByText('Manual')).toBeInTheDocument();
  });

  it('applies opacity-60 to archived rows', () => {
    renderTable([makeRow({ isActive: false })]);
    const row = screen.getByRole('row', { name: /Rent/ });
    expect(row.className).toContain('opacity-60');
  });
});
```

- [ ] **Step 2: Run to verify they fail**

```bash
pnpm test RecurringTable
```

Expected: module not found.

- [ ] **Step 3: Create stub `RecurringRowMenu.tsx` (placeholder so RecurringTable compiles)**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx
export function RecurringRowMenu(_props: unknown) { return null; }
```

- [ ] **Step 4: Create `RecurringTable.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringTable.tsx
import { Badge } from '@/components/ui/badge';
import { classifyStatus, type RecurringTransactionListItemDto } from './reminder-status';
import { RecurringRowMenu } from './RecurringRowMenu';

const FREQUENCY_LABELS: Record<string, string> = {
  Weekly: 'Weekly', Biweekly: 'Biweekly', Monthly: 'Monthly', Annual: 'Annual',
};

const STATUS_LABELS: Record<string, string> = {
  overdue: 'Overdue', dueToday: 'Due today', manual: 'Manual', archived: 'Archived',
};

const STATUS_VARIANTS: Record<string, 'destructive' | 'default' | 'secondary' | 'outline'> = {
  overdue: 'destructive', dueToday: 'default', manual: 'secondary', archived: 'secondary',
};

type Props = {
  rows: RecurringTransactionListItemDto[];
  today: string;
  onChanged: () => void;
};

export function RecurringTable({ rows, today, onChanged }: Props) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b text-muted-foreground text-xs uppercase tracking-wide">
          <th className="py-2 text-left font-medium">Name</th>
          <th className="w-32 py-2 text-left font-medium">Account</th>
          <th className="w-32 py-2 text-left font-medium">Category</th>
          <th className="w-24 py-2 text-left font-medium">Frequency</th>
          <th className="w-24 py-2 text-right font-medium tabular-nums">Est. amount</th>
          <th className="w-32 py-2 text-left font-medium">Next due</th>
          <th className="w-12 py-2" />
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <RecurringRow key={row.id} row={row} today={today} onChanged={onChanged} />
        ))}
      </tbody>
    </table>
  );
}

function RecurringRow({
  row, today, onChanged,
}: { row: RecurringTransactionListItemDto; today: string; onChanged: () => void }) {
  const statuses = classifyStatus(row, today);
  const archived = !row.isActive;

  return (
    <tr
      aria-label={row.name}
      className={`border-b last:border-0 ${archived ? 'opacity-60' : ''}`}
    >
      <td className="py-2 pr-4">
        <div className="flex items-center gap-2 flex-wrap">
          <span>{row.name}</span>
          {statuses.map((s) => (
            <Badge key={s} variant={STATUS_VARIANTS[s] ?? 'secondary'}>
              {STATUS_LABELS[s] ?? s}
            </Badge>
          ))}
        </div>
      </td>
      <td className="w-32 py-2 pr-4 truncate">{row.accountName}</td>
      <td className="w-32 py-2 pr-4 truncate" title={row.categoryName}>{row.categoryName}</td>
      <td className="w-24 py-2 pr-4">{FREQUENCY_LABELS[row.frequency] ?? row.frequency}</td>
      <td className="w-24 py-2 pr-4 text-right tabular-nums">
        {row.estimatedAmount == null
          ? '—'
          : `${row.currencySymbol}${row.estimatedAmount.toLocaleString()}`}
      </td>
      <td className="w-32 py-2 pr-4">{row.nextDueDate}</td>
      <td className="w-12 py-2 text-center">
        <RecurringRowMenu reminder={row} onChanged={onChanged} />
      </td>
    </tr>
  );
}
```

- [ ] **Step 5: Run `RecurringTable` tests**

```bash
pnpm test RecurringTable
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringTable.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringTable.test.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx
git commit -m "feat(recurring/client): add RecurringTable + tests (stub RecurringRowMenu)"
```

---

### Task 13: `RecurringConfirmDialog` + `RecurringDismissDialog`

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.test.tsx`

- [ ] **Step 1: Write failing Confirm dialog tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { RecurringConfirmDialog } from './RecurringConfirmDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

describe('RecurringConfirmDialog', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('populates date with reminder.nextDueDate on open', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect((screen.getByLabelText(/date \*/i) as HTMLInputElement).value).toBe(TODAY);
  });

  it('populates amount with estimatedAmount on open', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect((screen.getByLabelText(/amount \*/i) as HTMLInputElement).value).toBe('900');
  });

  it('shows Next due date field for ManualDate reminders', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder({ reminderBehaviour: 'ManualDate' })} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('hides Next due date field for Snap reminders', () => {
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.queryByLabelText(/next due date/i)).not.toBeInTheDocument();
  });

  it('calls POST on confirm and fires onChanged on 201', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      status: 201, ok: true, json: async () => ({ transactionId: 'tx-1' }),
    });
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={onChanged} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /confirm — record/i }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });

  it('shows inline error on 422 DATE_BEFORE_OPENING_BALANCE', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      status: 422, ok: false,
      json: async () => ({ error: { code: 'DATE_BEFORE_OPENING_BALANCE', message: 'Before opening balance.' } }),
    });
    render(<RecurringConfirmDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /confirm — record/i }));
    await waitFor(() => expect(screen.getByText(/opening balance/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Write failing Dismiss dialog tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { RecurringDismissDialog } from './RecurringDismissDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Gym', estimatedAmount: 40, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Wellness', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: TODAY,
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

describe('RecurringDismissDialog', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows simple confirm for Snap reminders (no next due date input)', () => {
    render(<RecurringDismissDialog open reminder={makeReminder()} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.queryByLabelText(/next due date/i)).not.toBeInTheDocument();
  });

  it('shows next due date input for ManualDate reminders', () => {
    render(<RecurringDismissDialog open reminder={makeReminder({ reminderBehaviour: 'ManualDate' })} onChanged={vi.fn()} onOpenChange={vi.fn()} />);
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('calls POST on dismiss and fires onChanged on 204', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204, ok: true });
    render(<RecurringDismissDialog open reminder={makeReminder()} onChanged={onChanged} onOpenChange={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /^dismiss$/i }));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });
});
```

- [ ] **Step 3: Run to verify they fail**

```bash
pnpm test RecurringConfirmDialog RecurringDismissDialog
```

Expected: module not found.

- [ ] **Step 4: Create `RecurringConfirmDialog.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.tsx
import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RECURRING_CONFIRM_URL } from './recurring-api';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  open: boolean;
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
  onOpenChange: (open: boolean) => void;
};

export function RecurringConfirmDialog({ open, reminder, onChanged, onOpenChange }: Props) {
  const [date, setDate] = useState(reminder.nextDueDate);
  const [amount, setAmount] = useState(reminder.estimatedAmount?.toString() ?? '');
  const [description, setDescription] = useState('');
  const [nextDueDate, setNextDueDate] = useState('');
  const [inlineError, setInlineError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isManual = reminder.reminderBehaviour === 'ManualDate';

  useEffect(() => {
    if (open) {
      setDate(reminder.nextDueDate);
      setAmount(reminder.estimatedAmount?.toString() ?? '');
      setDescription('');
      setNextDueDate('');
      setInlineError('');
    }
  }, [open, reminder]);

  async function handleConfirm() {
    setSubmitting(true);
    setInlineError('');
    try {
      const res = await fetch(RECURRING_CONFIRM_URL(reminder.id), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          date,
          amount: Number(amount),
          description: description.trim() || null,
          nextDueDate: isManual ? nextDueDate : null,
        }),
      });
      if (res.status === 201) {
        toast.success(`Recorded ${reminder.currencySymbol}${amount} on ${date}.`);
        onOpenChange(false);
        onChanged();
        return;
      }
      const body = await res.json().catch(() => ({})) as { error?: { code?: string } };
      if (body.error?.code === 'DATE_BEFORE_OPENING_BALANCE') {
        setInlineError("That date is before this account's opening balance.");
        return;
      }
      toast.error("Couldn't record. Try again.");
    } catch {
      toast.error("Couldn't record. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Confirm &apos;{reminder.name}&apos;?</AlertDialogTitle>
          <AlertDialogDescription>Record this reminder as a transaction.</AlertDialogDescription>
        </AlertDialogHeader>
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="confirm-date">Date *</Label>
            <Input id="confirm-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            {inlineError && <p className="text-sm text-destructive">{inlineError}</p>}
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="confirm-amount">Amount *</Label>
            <Input id="confirm-amount" type="number" min={0} step="0.01"
              value={amount} onChange={(e) => setAmount(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="confirm-desc">Description</Label>
            <Input id="confirm-desc" placeholder={reminder.name}
              value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          {isManual && (
            <div className="space-y-1.5">
              <Label htmlFor="confirm-next-due">Next due date *</Label>
              <Input id="confirm-next-due" type="date"
                value={nextDueDate} onChange={(e) => setNextDueDate(e.target.value)} />
            </div>
          )}
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleConfirm} disabled={submitting || !amount}>
            {submitting ? 'Confirming…' : 'Confirm — record'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 5: Create `RecurringDismissDialog.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.tsx
import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RECURRING_DISMISS_URL } from './recurring-api';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  open: boolean;
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
  onOpenChange: (open: boolean) => void;
};

export function RecurringDismissDialog({ open, reminder, onChanged, onOpenChange }: Props) {
  const [nextDueDate, setNextDueDate] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isManual = reminder.reminderBehaviour === 'ManualDate';

  useEffect(() => { if (open) setNextDueDate(''); }, [open]);

  async function handleDismiss() {
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_DISMISS_URL(reminder.id), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ nextDueDate: isManual ? nextDueDate : null }),
      });
      if (res.ok) {
        toast.success('Dismissed. Next due date advanced.');
        onOpenChange(false);
        onChanged();
        return;
      }
      toast.error("Couldn't dismiss. Try again.");
    } catch {
      toast.error("Couldn't dismiss. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  const description = isManual
    ? 'No transaction will be recorded. Pick the next due date manually.'
    : 'No transaction will be recorded. The next due date advances to the following period.';

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Dismiss &apos;{reminder.name}&apos;?</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        {isManual && (
          <div className="space-y-1.5">
            <Label htmlFor="dismiss-next-due">Next due date *</Label>
            <Input id="dismiss-next-due" type="date"
              value={nextDueDate} onChange={(e) => setNextDueDate(e.target.value)} />
          </div>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            onClick={handleDismiss}
            disabled={submitting || (isManual && !nextDueDate)}
          >
            {submitting ? 'Dismissing…' : 'Dismiss'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
```

- [ ] **Step 6: Run dialog tests**

```bash
pnpm test RecurringConfirmDialog RecurringDismissDialog
```

Expected: all pass.

- [ ] **Step 7: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringConfirmDialog.test.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringDismissDialog.test.tsx
git commit -m "feat(recurring/client): add Confirm and Dismiss dialogs + tests"
```

---

### Task 14: `RecurringRowMenu` — replace stub with full implementation

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.test.tsx`

- [ ] **Step 1: Write failing row menu tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.test.tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { RecurringRowMenu } from './RecurringRowMenu';
import type { RecurringTransactionListItemDto } from './reminder-status';

function makeReminder(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900, accountId: 'a', accountName: 'Checking',
    currencySymbol: '€', categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
    isActive: true, reminderBehaviour: 'SnapToCalendarDay', ...overrides,
  };
}

function renderMenu(reminder: RecurringTransactionListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter><RecurringRowMenu reminder={reminder} onChanged={onChanged} /></MemoryRouter>
  );
}

describe('RecurringRowMenu', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows Confirm, Edit, Dismiss, Archive for active row', async () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(screen.getByText('Confirm…')).toBeInTheDocument();
    expect(screen.getByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Dismiss…')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows only Reactivate for archived row', () => {
    renderMenu(makeReminder({ isActive: false }));
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(screen.getByText('Reactivate')).toBeInTheDocument();
    expect(screen.queryByText('Confirm…')).not.toBeInTheDocument();
  });

  it('opens Confirm dialog when Confirm… clicked', () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Confirm…'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText(/Confirm 'Rent'/)).toBeInTheDocument();
  });

  it('opens Archive dialog when Archive… clicked', () => {
    renderMenu(makeReminder());
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Archive…'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByText(/Archive 'Rent'/)).toBeInTheDocument();
  });

  it('calls Reactivate PATCH and fires onChanged on 204', async () => {
    const onChanged = vi.fn();
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204, ok: true });
    renderMenu(makeReminder({ isActive: false }), onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(screen.getByText('Reactivate'));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
  });
});
```

- [ ] **Step 2: Run to verify they fail**

```bash
pnpm test RecurringRowMenu
```

Expected: tests fail (stub returns null, no menu rendered).

- [ ] **Step 3: Replace stub with full `RecurringRowMenu.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { MoreHorizontal } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { RECURRING_ARCHIVE_URL, RECURRING_REACTIVATE_URL } from './recurring-api';
import { RecurringConfirmDialog } from './RecurringConfirmDialog';
import { RecurringDismissDialog } from './RecurringDismissDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
};

export function RecurringRowMenu({ reminder, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [dismissOpen, setDismissOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);

  async function handleReactivate() {
    try {
      const res = await fetch(RECURRING_REACTIVATE_URL(reminder.id), { method: 'PATCH' });
      if (res.ok) { toast.success('Reactivated.'); onChanged(); return; }
      toast.error("Couldn't reactivate. Try again.");
    } catch {
      toast.error("Couldn't reactivate. Try again.");
    }
  }

  async function handleArchive() {
    setArchiveOpen(false);
    try {
      const res = await fetch(RECURRING_ARCHIVE_URL(reminder.id), { method: 'PATCH' });
      if (res.ok) { toast.success('Archived.'); onChanged(); return; }
      toast.error("Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger render={
          <Button variant="ghost" size="icon" aria-label="Row actions">
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        } />
        <DropdownMenuContent align="end">
          {reminder.isActive ? (
            <>
              <DropdownMenuItem onClick={() => setConfirmOpen(true)}>Confirm…</DropdownMenuItem>
              <DropdownMenuItem onClick={() => navigate(`/recurring/${reminder.id}/edit`)}>Edit</DropdownMenuItem>
              <DropdownMenuItem onClick={() => setDismissOpen(true)}>Dismiss…</DropdownMenuItem>
              <DropdownMenuItem onClick={() => setArchiveOpen(true)}>Archive…</DropdownMenuItem>
            </>
          ) : (
            <DropdownMenuItem onClick={handleReactivate}>Reactivate</DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <RecurringConfirmDialog
        open={confirmOpen} reminder={reminder} onChanged={onChanged} onOpenChange={setConfirmOpen}
      />

      <RecurringDismissDialog
        open={dismissOpen} reminder={reminder} onChanged={onChanged} onOpenChange={setDismissOpen}
      />

      <AlertDialog open={archiveOpen} onOpenChange={setArchiveOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive &apos;{reminder.name}&apos;?</AlertDialogTitle>
            <AlertDialogDescription>
              This reminder will stop appearing in the active list and will not advance any further.
              Existing transactions stay attached to your account history. You can reactivate it
              later from the archived list.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>Archive</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
```

- [ ] **Step 4: Run row menu tests**

```bash
pnpm test RecurringRowMenu
```

Expected: all pass.

- [ ] **Step 5: Run all client tests**

```bash
pnpm test
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringRowMenu.test.tsx
git commit -m "feat(recurring/client): replace RecurringRowMenu stub with full implementation + tests"
```

---

### Task 15: `RecurringForm` — shared Create/Edit form with conditional day picker

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringForm.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringForm.test.tsx`

- [ ] **Step 1: Check the CategoryListItemDto type shape**

```bash
grep -n "CategoryListItemDto\|export type" ProjectCeres.Client/src/app/features/categories/categories-api.ts | head -20
```

Note the exact field names for use in the stub below.

- [ ] **Step 2: Write failing form tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringForm.test.tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { RecurringForm } from './RecurringForm';
import type { AccountListItemDto } from '../accounts/accounts-api';

// Minimal stubs — adjust field names to match actual types if grep above shows differences
const accounts: AccountListItemDto[] = [
  { id: 'a1', name: 'Checking', accountTypeId: 1, accountTypeName: 'Asset', currencyId: 1,
    currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,
    excludeFromSpendable: false, excludeFromReports: false, liabilityRepaymentType: null,
    interestRate: null, balance: 0, hasTransactions: false },
];

// CategoryListItemDto stub — shape verified by Step 1 grep
const categories = [{ id: 'c1', name: 'Housing' }] as any[];

const defaults = {
  name: '', accountId: 'a1', categoryId: 'c1', estimatedAmount: '',
  frequency: 'Monthly', reminderBehaviour: 'SnapToCalendarDay',
  dayOfPeriod: null as number | null, nextDueDate: '2026-05-03',
};

describe('RecurringForm', () => {
  const noop = vi.fn();

  it('renders Name, EstimatedAmount, NextDueDate fields', () => {
    render(<RecurringForm values={defaults} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.getByLabelText(/name \*/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/estimated amount/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/next due date \*/i)).toBeInTheDocument();
  });

  it('shows Day of month input for Snap + Monthly', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Monthly', reminderBehaviour: 'SnapToCalendarDay' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.getByLabelText(/day of month \*/i)).toBeInTheDocument();
  });

  it('shows Day of week picker for Snap + Weekly', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Weekly', reminderBehaviour: 'SnapToCalendarDay' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.getByLabelText(/day of week \*/i)).toBeInTheDocument();
  });

  it('hides day picker for Snap + Annual', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Annual', reminderBehaviour: 'SnapToCalendarDay' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.queryByLabelText(/day of (week|month)/i)).not.toBeInTheDocument();
  });

  it('hides day picker for ManualDate regardless of frequency', () => {
    render(<RecurringForm values={{ ...defaults, frequency: 'Monthly', reminderBehaviour: 'ManualDate' }} onChange={noop} accounts={accounts} categories={categories} />);
    expect(screen.queryByLabelText(/day of (week|month)/i)).not.toBeInTheDocument();
  });

  it('calls onChange with updated name on input', () => {
    const onChange = vi.fn();
    render(<RecurringForm values={defaults} onChange={onChange} accounts={accounts} categories={categories} />);
    fireEvent.change(screen.getByLabelText(/name \*/i), { target: { value: 'Rent' } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: 'Rent' }));
  });
});
```

- [ ] **Step 3: Run to verify they fail**

```bash
pnpm test RecurringForm
```

Expected: module not found.

- [ ] **Step 4: Create `RecurringForm.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringForm.tsx
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import type { AccountListItemDto } from '../accounts/accounts-api';

export type RecurringFormValues = {
  name: string;
  accountId: string;
  categoryId: string;
  estimatedAmount: string;        // empty string = null on submit
  frequency: string;
  reminderBehaviour: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
};

type Props = {
  values: RecurringFormValues;
  onChange: (updated: RecurringFormValues) => void;
  accounts: AccountListItemDto[];
  categories: Array<{ id: string; name: string }>;
};

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly', 'Annual'];
const BEHAVIOURS = [
  { value: 'SnapToCalendarDay', label: 'Snap to calendar day' },
  { value: 'RelativeToLastConfirmation', label: 'Relative to last confirmation' },
  { value: 'ManualDate', label: 'Manual date' },
];
const WEEKDAYS = [
  { value: 1, label: 'Monday' }, { value: 2, label: 'Tuesday' },
  { value: 3, label: 'Wednesday' }, { value: 4, label: 'Thursday' },
  { value: 5, label: 'Friday' }, { value: 6, label: 'Saturday' }, { value: 7, label: 'Sunday' },
];

function showDayOfWeek(frequency: string, behaviour: string) {
  return behaviour === 'SnapToCalendarDay' && (frequency === 'Weekly' || frequency === 'Biweekly');
}
function showDayOfMonth(frequency: string, behaviour: string) {
  return behaviour === 'SnapToCalendarDay' && frequency === 'Monthly';
}

export function RecurringForm({ values, onChange, accounts, categories }: Props) {
  function set(patch: Partial<RecurringFormValues>) {
    const next = { ...values, ...patch };
    if (!showDayOfWeek(next.frequency, next.reminderBehaviour) &&
        !showDayOfMonth(next.frequency, next.reminderBehaviour)) {
      next.dayOfPeriod = null;
    }
    onChange(next);
  }

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="rt-name">Name *</Label>
        <Input id="rt-name" value={values.name} onChange={(e) => set({ name: e.target.value })} />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-account">Account *</Label>
        <Select value={values.accountId} onValueChange={(v) => set({ accountId: v })}>
          <SelectTrigger id="rt-account"><SelectValue placeholder="Select account" /></SelectTrigger>
          <SelectContent>
            {accounts.map((a) => <SelectItem key={a.id} value={a.id}>{a.name}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-category">Category *</Label>
        <Select value={values.categoryId} onValueChange={(v) => set({ categoryId: v })}>
          <SelectTrigger id="rt-category"><SelectValue placeholder="Select category" /></SelectTrigger>
          <SelectContent>
            {categories.map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-amount">Estimated amount</Label>
        <Input id="rt-amount" type="number" min={0} step="0.01"
          value={values.estimatedAmount}
          onChange={(e) => set({ estimatedAmount: e.target.value })} />
        <p className="text-xs text-muted-foreground">Leave blank if the amount varies each time.</p>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-frequency">Frequency *</Label>
        <Select value={values.frequency} onValueChange={(v) => set({ frequency: v })}>
          <SelectTrigger id="rt-frequency"><SelectValue /></SelectTrigger>
          <SelectContent>
            {FREQUENCIES.map((f) => <SelectItem key={f} value={f}>{f}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-behaviour">Reminder behaviour *</Label>
        <Select value={values.reminderBehaviour} onValueChange={(v) => set({ reminderBehaviour: v })}>
          <SelectTrigger id="rt-behaviour"><SelectValue /></SelectTrigger>
          <SelectContent>
            {BEHAVIOURS.map((b) => <SelectItem key={b.value} value={b.value}>{b.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      {showDayOfWeek(values.frequency, values.reminderBehaviour) && (
        <div className="space-y-1.5">
          <Label htmlFor="rt-dow">Day of week *</Label>
          <Select
            value={values.dayOfPeriod?.toString() ?? ''}
            onValueChange={(v) => set({ dayOfPeriod: Number(v) })}
          >
            <SelectTrigger id="rt-dow"><SelectValue placeholder="Pick a day" /></SelectTrigger>
            <SelectContent>
              {WEEKDAYS.map((d) => <SelectItem key={d.value} value={d.value.toString()}>{d.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
      )}

      {showDayOfMonth(values.frequency, values.reminderBehaviour) && (
        <div className="space-y-1.5">
          <Label htmlFor="rt-dom">Day of month *</Label>
          <Input id="rt-dom" type="number" min={1} max={31}
            value={values.dayOfPeriod?.toString() ?? ''}
            onChange={(e) => set({ dayOfPeriod: e.target.value ? Number(e.target.value) : null })} />
          <p className="text-xs text-muted-foreground">1–31. Snaps to the last day if the month is shorter.</p>
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="rt-nextdue">Next due date *</Label>
        <Input id="rt-nextdue" type="date"
          value={values.nextDueDate} onChange={(e) => set({ nextDueDate: e.target.value })} />
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Run form tests**

```bash
pnpm test RecurringForm
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringForm.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringForm.test.tsx
git commit -m "feat(recurring/client): add RecurringForm with conditional day picker + tests"
```

---

### Task 16: `RecurringCreate` + `RecurringEdit` — page glues

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringCreate.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringCreate.test.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringEdit.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringEdit.test.tsx`

- [ ] **Step 1: Write failing Create tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringCreate.test.tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringCreate } from './RecurringCreate';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

function renderCreate() {
  return render(
    <MemoryRouter initialEntries={['/recurring/new']}>
      <Routes>
        <Route path="recurring/new" element={<RecurringCreate ctx={CTX} />} />
        <Route path="recurring" element={<div>List page</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringCreate', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
  });

  it('renders the form card with title "New reminder"', async () => {
    renderCreate();
    await waitFor(() => expect(screen.getByText('New reminder')).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Write failing Edit tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringEdit.test.tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringEdit } from './RecurringEdit';

const CTX = { refetch: vi.fn(), refreshBell: vi.fn() };

describe('RecurringEdit', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('renders form pre-populated with reminder name', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (url.includes('/api/recurring-transactions/r1')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({
          id: 'r1', name: 'Rent', estimatedAmount: 900, accountId: 'a1', categoryId: 'c1',
          frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-15',
          isActive: true, reminderBehaviour: 'SnapToCalendarDay',
        }) });
      }
      return Promise.resolve({ ok: true, json: async () => [] });
    });
    render(
      <MemoryRouter initialEntries={['/recurring/r1/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
          <Route path="recurring" element={<div>List</div>} />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() =>
      expect((screen.getByLabelText(/name \*/i) as HTMLInputElement).value).toBe('Rent')
    );
  });

  it('shows not-found banner on 404', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false, status: 404 });
    render(
      <MemoryRouter initialEntries={['/recurring/unknown/edit']}>
        <Routes>
          <Route path="recurring/:id/edit" element={<RecurringEdit ctx={CTX} />} />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getByText(/doesn't exist/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 3: Run to verify they fail**

```bash
pnpm test RecurringCreate RecurringEdit
```

Expected: module not found.

- [ ] **Step 4: Create `RecurringCreate.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringCreate.tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_URL, type AccountListItemDto } from '../accounts/accounts-api';
import { RecurringForm, type RecurringFormValues } from './RecurringForm';
import { RECURRING_URL } from './recurring-api';

export type RecurringPageCtx = { refetch: () => void; refreshBell: () => void };

const TODAY = new Date().toISOString().slice(0, 10);

const DEFAULTS: RecurringFormValues = {
  name: '', accountId: '', categoryId: '', estimatedAmount: '',
  frequency: 'Monthly', reminderBehaviour: 'SnapToCalendarDay',
  dayOfPeriod: null, nextDueDate: TODAY,
};

export function RecurringCreate({ ctx }: { ctx: RecurringPageCtx }) {
  const navigate = useNavigate();
  const accounts = useApi<AccountListItemDto[]>(ACCOUNTS_URL);
  const categories = useApi<Array<{ id: string; name: string }>>('/api/categories');
  const [values, setValues] = useState<RecurringFormValues>(DEFAULTS);
  const [submitting, setSubmitting] = useState(false);

  const loading = accounts.loading || categories.loading;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: values.name.trim(),
          estimatedAmount: values.estimatedAmount === '' ? null : Number(values.estimatedAmount),
          accountId: values.accountId,
          categoryId: values.categoryId,
          frequency: values.frequency,
          dayOfPeriod: values.dayOfPeriod,
          nextDueDate: values.nextDueDate,
          reminderBehaviour: values.reminderBehaviour,
        }),
      });
      if (res.ok) {
        toast.success('Created.');
        ctx.refetch();
        ctx.refreshBell();
        navigate('/recurring');
        return;
      }
      toast.error("Couldn't save. Try again.");
    } catch {
      toast.error("Couldn't save. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <Card>
        <CardHeader><CardTitle>New reminder</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader><CardTitle>New reminder</CardTitle></CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="space-y-4">
          <RecurringForm
            values={values}
            onChange={setValues}
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
          />
          <div className="flex gap-2 pt-2">
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saving…' : 'Save'}
            </Button>
            <Button type="button" variant="outline" onClick={() => navigate('/recurring')}>
              Cancel
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 5: Create `RecurringEdit.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringEdit.tsx
import { useState, useEffect } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_URL, type AccountListItemDto } from '../accounts/accounts-api';
import { RecurringForm, type RecurringFormValues } from './RecurringForm';
import { RECURRING_BY_ID_URL, type RecurringTransactionDetailDto } from './recurring-api';
import type { RecurringPageCtx } from './RecurringCreate';

export function RecurringEdit({ ctx }: { ctx: RecurringPageCtx }) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const detail = useApi<RecurringTransactionDetailDto>(RECURRING_BY_ID_URL(id!));
  const accounts = useApi<AccountListItemDto[]>(ACCOUNTS_URL);
  const categories = useApi<Array<{ id: string; name: string }>>('/api/categories');
  const [values, setValues] = useState<RecurringFormValues | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [notFound, setNotFound] = useState(false);

  useEffect(() => {
    if (detail.data) {
      setValues({
        name: detail.data.name,
        accountId: detail.data.accountId,
        categoryId: detail.data.categoryId,
        estimatedAmount: detail.data.estimatedAmount?.toString() ?? '',
        frequency: detail.data.frequency,
        reminderBehaviour: detail.data.reminderBehaviour,
        dayOfPeriod: detail.data.dayOfPeriod,
        nextDueDate: detail.data.nextDueDate,
      });
    }
    if (detail.error) setNotFound(true);
  }, [detail.data, detail.error]);

  const loading = detail.loading || accounts.loading || categories.loading;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!values) return;
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_BY_ID_URL(id!), {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: values.name.trim(),
          estimatedAmount: values.estimatedAmount === '' ? null : Number(values.estimatedAmount),
          accountId: values.accountId,
          categoryId: values.categoryId,
          frequency: values.frequency,
          dayOfPeriod: values.dayOfPeriod,
          nextDueDate: values.nextDueDate,
          reminderBehaviour: values.reminderBehaviour,
        }),
      });
      if (res.ok) {
        toast.success('Saved.');
        ctx.refetch();
        ctx.refreshBell();
        navigate('/recurring');
        return;
      }
      toast.error("Couldn't save. Try again.");
    } catch {
      toast.error("Couldn't save. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (notFound) {
    return (
      <Card>
        <CardContent className="pt-6 space-y-3">
          <p>That reminder doesn&apos;t exist.</p>
          <Button variant="outline" onClick={() => navigate('/recurring')}>← Back to Recurring</Button>
        </CardContent>
      </Card>
    );
  }

  if (loading || !values) {
    return (
      <Card>
        <CardHeader><CardTitle>Edit reminder</CardTitle></CardHeader>
        <CardContent className="space-y-3">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader><CardTitle>Edit reminder</CardTitle></CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit} className="space-y-4">
          <RecurringForm
            values={values}
            onChange={setValues}
            accounts={accounts.data ?? []}
            categories={categories.data ?? []}
          />
          <div className="flex gap-2 pt-2">
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saving…' : 'Save'}
            </Button>
            <Button type="button" variant="outline" onClick={() => navigate('/recurring')}>
              Cancel
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 6: Run page tests**

```bash
pnpm test RecurringCreate RecurringEdit
```

Expected: all pass.

- [ ] **Step 7: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringCreate.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringCreate.test.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringEdit.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringEdit.test.tsx
git commit -m "feat(recurring/client): add RecurringCreate and RecurringEdit pages + tests"
```

---

### Task 17: `RecurringLayout` — list page, search, empty states

**Files:**
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx`
- Create: `ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx`

- [ ] **Step 1: Write failing layout tests**

```typescript
// ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { RecurringLayout } from './RecurringLayout';

function renderLayout(path = '/recurring') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="recurring" element={<RecurringLayout />}>
          <Route path="new" element={<div>Create form</div>} />
          <Route path=":id/edit" element={<div>Edit form</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe('RecurringLayout', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('shows skeleton while loading', () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}));
    renderLayout();
    expect(screen.getByTestId('recurring-skeleton')).toBeInTheDocument();
  });

  it('renders h1 "Recurring transactions"', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders first-run empty state when no reminders exist', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout();
    await waitFor(() => expect(screen.getByText(/No recurring reminders yet/i)).toBeInTheDocument());
  });

  it('renders child route when on /recurring/new (hides list chrome)', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => [] });
    renderLayout('/recurring/new');
    await waitFor(() => expect(screen.getByText('Create form')).toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Recurring transactions' })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify they fail**

```bash
pnpm test RecurringLayout
```

Expected: module not found.

- [ ] **Step 3: Create `RecurringLayout.tsx`**

```tsx
// ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Outlet, useMatch, useOutletContext, useSearchParams } from 'react-router-dom';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useDebounced } from '../../lib/use-debounced';
import { RecurringTable } from './RecurringTable';
import { buildListUrl, type RecurringTransactionListItemDto } from './recurring-api';

const TODAY = new Date().toISOString().slice(0, 10);

export type RecurringLayoutCtx = { refetch: () => void; refreshBell: () => void };

export function useRecurringLayoutCtx() {
  return useOutletContext<RecurringLayoutCtx>();
}

export function RecurringLayout() {
  const [params, setParams] = useSearchParams();
  const onNew  = !!useMatch('/recurring/new');
  const onEdit = !!useMatch('/recurring/:id/edit');
  const childActive = onNew || onEdit;

  const includeInactive = params.get('includeInactive') === 'true';
  const queryParam = params.get('q') ?? '';

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  const [searchInput, setSearchInput] = useState(queryParam);
  const debouncedSearch = useDebounced(searchInput, 200);

  useEffect(() => {
    const next = new URLSearchParams(params);
    if (debouncedSearch) next.set('q', debouncedSearch);
    else next.delete('q');
    setParams(next, { replace: true });
  }, [debouncedSearch]);  // eslint-disable-line react-hooks/exhaustive-deps

  const list = useApi<RecurringTransactionListItemDto[]>(buildListUrl(includeInactive));
  const allList = useApi<RecurringTransactionListItemDto[]>('/api/recurring-transactions?includeInactive=true');

  const refreshBell = () => {};  // replaced by ReminderCountProvider in Task 18
  const ctx: RecurringLayoutCtx = { refetch: list.refetch, refreshBell };

  if (childActive) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <Outlet context={ctx} />
      </div>
    );
  }

  function setIncludeInactive(checked: boolean) {
    const p = new URLSearchParams(params);
    if (checked) p.set('includeInactive', 'true');
    else p.delete('includeInactive');
    setParams(p, { replace: true });
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <header>
        <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
          Recurring transactions
        </h1>
        <p className="mt-2 text-muted-foreground">
          Templates that confirm into real transactions on a schedule. Confirm a reminder when it
          actually happens; dismiss it when you want to skip.
        </p>
      </header>

      <Card>
        <CardContent className="pt-6 space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
            <div className="flex-1 space-y-1.5">
              <Label htmlFor="recurring-search" className="sr-only">Filter reminders</Label>
              <Input
                id="recurring-search"
                placeholder="Filter reminders…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
              />
            </div>
            <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New reminder</Link>} />
          </div>
          <div className="flex items-center gap-2">
            <Switch id="include-archived" checked={includeInactive} onCheckedChange={setIncludeInactive} />
            <Label htmlFor="include-archived" className="text-sm font-normal">Include archived</Label>
          </div>

          <RecurringBody
            list={list}
            allList={allList}
            query={debouncedSearch}
            onClearSearch={() => setSearchInput('')}
            onChanged={list.refetch}
          />
        </CardContent>
      </Card>
    </div>
  );
}

type BodyProps = {
  list: ReturnType<typeof useApi<RecurringTransactionListItemDto[]>>;
  allList: ReturnType<typeof useApi<RecurringTransactionListItemDto[]>>;
  query: string;
  onClearSearch: () => void;
  onChanged: () => void;
};

function RecurringBody({ list, allList, query, onClearSearch, onChanged }: BodyProps) {
  const lower = query.toLowerCase();

  const sorted = useMemo(() => {
    if (!list.data) return [];
    return [...list.data]
      .filter((r) => r.name.toLowerCase().includes(lower))
      .sort((a, b) => {
        if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
        return a.nextDueDate.localeCompare(b.nextDueDate);
      });
  }, [list.data, lower]);

  if (list.loading) {
    return (
      <div data-testid="recurring-skeleton" className="space-y-2 py-2">
        {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
      </div>
    );
  }

  if (list.error || !list.data) {
    return <CardError section="Recurring transactions" onRetry={list.refetch} />;
  }

  if (sorted.length === 0 && query === '') {
    const anyExist = (allList.data?.length ?? 0) > 0;
    if (!anyExist) {
      return (
        <div className="px-3 py-12 text-center space-y-3">
          <h2 className="text-lg font-semibold">No recurring reminders yet</h2>
          <p className="text-sm text-muted-foreground">
            Set up reminders for bills, subscriptions, salary — anything that recurs.
          </p>
          <Button render={<Link to="new"><Plus className="h-4 w-4 mr-1" />New reminder</Link>} />
        </div>
      );
    }
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No active reminders.
      </div>
    );
  }

  if (sorted.length === 0 && query) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic space-y-3">
        <p>No reminders match &apos;{query}&apos;.</p>
        <Button type="button" variant="link" onClick={onClearSearch}>Clear search</Button>
      </div>
    );
  }

  return <RecurringTable rows={sorted} today={TODAY} onChanged={onChanged} />;
}
```

- [ ] **Step 4: Run layout tests**

```bash
pnpm test RecurringLayout
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/features/recurring/RecurringLayout.tsx \
        ProjectCeres.Client/src/app/features/recurring/RecurringLayout.test.tsx
git commit -m "feat(recurring/client): add RecurringLayout (list page) + tests"
```

---

### Task 18: `ReminderCountProvider` + `TopBar` bell rewrite

**Files:**
- Create: `ProjectCeres.Client/src/app/layout/ReminderCountProvider.tsx`
- Create: `ProjectCeres.Client/src/app/layout/ReminderCountProvider.test.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/TopBar.tsx`
- Modify: `ProjectCeres.Client/src/app/layout/TopBar.test.tsx`

- [ ] **Step 1: Write failing `ReminderCountProvider` tests**

```typescript
// ProjectCeres.Client/src/app/layout/ReminderCountProvider.test.tsx
import { render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ReminderCountProvider, useReminderCount } from './ReminderCountProvider';

function CountDisplay() {
  const { count, reminders } = useReminderCount();
  return <div data-testid="count">{count} / {reminders.length}</div>;
}

describe('ReminderCountProvider', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  it('provides count from /api/recurring-transactions/upcoming?days=0', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => [{ id: '1' }, { id: '2' }],
    });
    render(<ReminderCountProvider><CountDisplay /></ReminderCountProvider>);
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('2 / 2'));
  });

  it('empty list yields count=0', async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      ok: true, json: async () => [],
    });
    render(<ReminderCountProvider><CountDisplay /></ReminderCountProvider>);
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('0 / 0'));
  });
});
```

- [ ] **Step 2: Run to verify they fail**

```bash
pnpm test ReminderCountProvider
```

Expected: module not found.

- [ ] **Step 3: Create `ReminderCountProvider.tsx`**

```tsx
// ProjectCeres.Client/src/app/layout/ReminderCountProvider.tsx
import { createContext, useContext, useMemo } from 'react';
import { useApi } from '../lib/use-api';
import type { RecurringTransactionListItemDto } from '../features/recurring/reminder-status';

type Ctx = {
  count: number;
  reminders: RecurringTransactionListItemDto[];
  loading: boolean;
  refresh: () => void;
};

const ReminderCountContext = createContext<Ctx>({
  count: 0, reminders: [], loading: false, refresh: () => {},
});

export function ReminderCountProvider({ children }: { children: React.ReactNode }) {
  const api = useApi<RecurringTransactionListItemDto[]>('/api/recurring-transactions/upcoming?days=0');
  const value = useMemo<Ctx>(() => ({
    count: api.data?.length ?? 0,
    reminders: api.data ?? [],
    loading: api.loading,
    refresh: api.refetch,
  }), [api.data, api.loading, api.refetch]);

  return <ReminderCountContext.Provider value={value}>{children}</ReminderCountContext.Provider>;
}

export function useReminderCount(): Ctx {
  return useContext(ReminderCountContext);
}
```

- [ ] **Step 4: Run provider tests**

```bash
pnpm test ReminderCountProvider
```

Expected: all pass.

- [ ] **Step 5: Locate the `NotificationsButton` stub in `TopBar.tsx`**

```bash
grep -n "Notification\|notifications\|Bell\|no notification\|stub" \
  ProjectCeres.Client/src/app/layout/TopBar.tsx | head -20
```

Read the identified lines to understand the current stub shape before modifying.

- [ ] **Step 6: Replace `NotificationsButton` in `TopBar.tsx`**

Add these imports at the top of `TopBar.tsx` (skip any already present):

```typescript
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { useReminderCount } from './ReminderCountProvider';
```

Replace the `NotificationsButton` function body:

```tsx
function NotificationsButton() {
  const { count, reminders, loading, refresh } = useReminderCount();
  const [open, setOpen] = useState(false);

  function handleOpenChange(next: boolean) {
    setOpen(next);
    if (next) refresh();
  }

  return (
    <Popover open={open} onOpenChange={handleOpenChange}>
      <PopoverTrigger render={
        <Button
          variant="ghost"
          size="icon"
          aria-label={count > 0 ? `Notifications, ${count} due` : 'Notifications'}
          className="relative"
        >
          <Bell className="h-5 w-5" />
          {count > 0 && (
            <span
              aria-hidden
              className="absolute -top-0.5 -right-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-destructive text-[10px] text-destructive-foreground font-semibold"
            >
              {count > 9 ? '9+' : count}
            </span>
          )}
        </Button>
      } />
      <PopoverContent align="end" className="w-80 p-0">
        <div className="px-3 py-2 border-b text-sm font-medium">Reminders</div>
        {loading ? (
          <div className="px-3 py-4 space-y-2">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-3/4" />
          </div>
        ) : reminders.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted-foreground">
            Nothing due. You&apos;re all caught up.
          </p>
        ) : (
          <ul className="max-h-72 overflow-y-auto divide-y">
            {reminders.map((r) => (
              <li key={r.id} className="px-3 py-2">
                <Link
                  to="/recurring"
                  onClick={() => setOpen(false)}
                  className="text-sm hover:underline block"
                >
                  {r.name} — {r.nextDueDate}
                </Link>
              </li>
            ))}
          </ul>
        )}
        <div className="px-3 py-2 border-t">
          <Button variant="link" size="sm" render={
            <Link to="/recurring" onClick={() => setOpen(false)}>View all reminders →</Link>
          } />
        </div>
      </PopoverContent>
    </Popover>
  );
}
```

- [ ] **Step 7: Add TopBar bell tests**

Open `TopBar.test.tsx`. At the end of the file, append:

```typescript
// Bell behaviour — append after existing test suite
import { ReminderCountProvider } from './ReminderCountProvider';

describe('TopBar — bell badge', () => {
  beforeEach(() => { global.fetch = vi.fn(); });

  function renderWithCount(count: number) {
    const items = Array.from({ length: count }, (_, i) => ({
      id: String(i), name: `R${i}`, estimatedAmount: null, accountId: 'a', accountName: 'A',
      currencySymbol: '€', categoryId: 'c', categoryName: 'C', categoryTypeName: 'Expense',
      frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-05-03',
      isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    }));
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: true, json: async () => items });
    // Substitute the actual TopBar render call matching the file's existing test pattern
    // e.g.: render(<MemoryRouter><ReminderCountProvider><TopBar /></ReminderCountProvider></MemoryRouter>);
  }

  it('shows no badge when count=0', async () => {
    renderWithCount(0);
    // assert no numeric badge in the bell button
  });

  it('shows badge count when count > 0', async () => {
    renderWithCount(3);
    await waitFor(() => expect(screen.getByText('3')).toBeInTheDocument());
  });

  it('shows 9+ when count > 9', async () => {
    renderWithCount(12);
    await waitFor(() => expect(screen.getByText('9+')).toBeInTheDocument());
  });
});
```

> **Note:** Open `TopBar.test.tsx` first to see how existing tests render the component, then adapt the `renderWithCount` helper to match that pattern exactly.

- [ ] **Step 8: Run TopBar tests**

```bash
pnpm test TopBar ReminderCountProvider
```

Expected: existing TopBar tests still pass; new bell tests pass.

- [ ] **Step 9: Run full client test suite**

```bash
pnpm test
```

Expected: all green.

- [ ] **Step 10: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/layout/ReminderCountProvider.tsx \
        ProjectCeres.Client/src/app/layout/ReminderCountProvider.test.tsx \
        ProjectCeres.Client/src/app/layout/TopBar.tsx \
        ProjectCeres.Client/src/app/layout/TopBar.test.tsx
git commit -m "feat(recurring/client): ReminderCountProvider + topbar bell wiring + tests"
```

---

### Task 19: Wire routes in `App.tsx` + update `Recurring.tsx` + update `App.test.tsx`

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Recurring.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx`
- Modify: `ProjectCeres.Client/src/app/App.test.tsx`

> **Context:** `App.tsx:50` has `<Route path="recurring" element={<Recurring />} />` (flat). Must become nested with `/new` and `/:id/edit`. Shell must wrap in `<ReminderCountProvider>`. Current `Recurring.tsx` is a placeholder — replace with re-export.

- [ ] **Step 1: Update `Recurring.tsx`**

Replace entire file contents:

```tsx
// ProjectCeres.Client/src/app/pages/Recurring.tsx
export { RecurringLayout as Recurring } from '../features/recurring/RecurringLayout';
```

- [ ] **Step 2: Update `App.tsx`**

Add to the import block:

```typescript
import { RecurringCreate } from './features/recurring/RecurringCreate';
import { RecurringEdit } from './features/recurring/RecurringEdit';
import { useRecurringLayoutCtx } from './features/recurring/RecurringLayout';
import { ReminderCountProvider } from './layout/ReminderCountProvider';
```

Change the flat recurring route (line 50):

```tsx
// Before:
<Route path="recurring" element={<Recurring />} />

// After:
<Route path="recurring" element={<Recurring />}>
  <Route path="new" element={<RecurringCreateBridge />} />
  <Route path=":id/edit" element={<RecurringEditBridge />} />
</Route>
```

Wrap the `<Routes>` in the return value with `<ReminderCountProvider>`:

```tsx
export function App() {
  return (
    <ReminderCountProvider>
      <Routes>
        {/* ... unchanged ... */}
      </Routes>
    </ReminderCountProvider>
  );
}
```

Add bridge components at the module level (after imports, before or after `App`):

```tsx
function RecurringCreateBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringCreate ctx={ctx} />;
}

function RecurringEditBridge() {
  const ctx = useRecurringLayoutCtx();
  return <RecurringEdit ctx={ctx} />;
}
```

- [ ] **Step 3: Update `App.test.tsx`**

Open the file and find the test that covers the `/recurring` route. Update it to expect the live list page h1:

```typescript
it('renders recurring list page at /recurring', async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
  // use the existing render helper pattern in the file — adapt as needed
  await waitFor(() =>
    expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
  );
});
```

- [ ] **Step 4: Run all client tests**

```bash
pnpm test
```

Expected: all green.

- [ ] **Step 5: Type-check + production build**

```bash
pnpm build
```

Expected: succeeds with no type errors.

- [ ] **Step 6: Commit**

```bash
cd <repo>
git add ProjectCeres.Client/src/app/pages/Recurring.tsx \
        ProjectCeres.Client/src/app/App.tsx \
        ProjectCeres.Client/src/app/App.test.tsx
git commit -m "feat(recurring/client): wire nested routes + ReminderCountProvider in App.tsx"
```

---

### Task 20: Documentation sync + verification gate + final commit

**Files:**
- Modify: `docs/planning-phase3-spa-migration.md`
- Modify: `docs/planning-phase3.md`
- Modify: `docs/api-contract.md`

> **Before any doc change:** open and grep for the relevant section to confirm it's absent or outdated. Only write what's confirmed missing.

- [ ] **Step 1: Invoke sync-docs**

```
/sync-docs
```

Review each proposed change. Apply ones confirmed absent.

- [ ] **Step 2: Apply targeted doc changes**

**`docs/planning-phase3-spa-migration.md`:**
- §2 controller table: `RecurringTransactionsController` row → "Migrated 2026-05-03".
- §8 batch table: Recurring row → "✅ Migrated 2026-05-03".

**`docs/planning-phase3.md`:**
- §14: add "✓ **Recurring transactions — list + Create/Edit + Confirm/Dismiss/Archive/Reactivate + topbar bell wiring + Weekly/Biweekly Snap fix** (2026-05-03)".

**`docs/api-contract.md`:**
- Add `PATCH /api/recurring-transactions/:id/reactivate` → 204 No Content.
- Document `EstimatedAmount` as `decimal?` (null = "amount varies").
- Document `POST /api/recurring-transactions/:id/dismiss` optional body `{ nextDueDate?: string }` for ManualDate reminders.

- [ ] **Step 3: Run full verification gate**

```bash
dotnet test
cd ProjectCeres.Client && pnpm test && pnpm build
cd ..
```

Expected: all green, build succeeds.

- [ ] **Step 4: Manual click-through** (user step)

Per spec §10 verification checklist:
- [ ] List page renders — 6 columns, status badges, topbar bell count
- [ ] Topbar bell popover lists reminders; "View all" → /recurring
- [ ] Search, clear, Include archived all work
- [ ] Confirm dialog: defaults, ManualDate extra field, success toast, NextDueDate advances
- [ ] Dismiss dialog: Snap simple, ManualDate requires NextDueDate
- [ ] Archive → row disappears; Reactivate → row returns
- [ ] Create form: conditional day picker per Frequency+Behaviour
- [ ] Razor redirects: `/RecurringTransactions` → 302 → `/app/recurring`

- [ ] **Step 5: Commit docs sync**

```bash
git add docs/planning-phase3-spa-migration.md \
        docs/planning-phase3.md \
        docs/api-contract.md
git commit -m "docs(spa): sync docs after Recurring SPA migration"
```

---

## Self-Review Checklist

### Spec coverage

| Spec section | Covered by task(s) |
|---|---|
| §2 Goals — list + CRUD | Tasks 12, 15, 16, 17 |
| §2 Goals — Snap fix | Task 2 |
| §2 Goals — RecurringTransactionPolicies | Tasks 1, 4 |
| §2 Goals — Reactivate endpoint | Task 5 |
| §2 Goals — EstimatedAmount nullable | Task 3 |
| §2 Goals — Razor cutover | Task 7 |
| §2 Goals — Topbar bell | Task 18 |
| §3 File structure | All tasks map to spec §3 |
| §4 Routing | Tasks 17 (Outlet), 19 (App.tsx) |
| §5 List page — columns, badges, sort, empty states | Tasks 12, 17 |
| §6 Form — conditional day pickers, normalisation | Task 15 |
| §7 Row menu — Confirm, Dismiss, Archive, Reactivate | Tasks 13, 14 |
| §8a Weekly+Biweekly Snap | Task 2 |
| §8b RecurringTransactionPolicies | Tasks 1, 4 |
| §8c Reactivate | Task 5 |
| §8d EstimatedAmount nullability | Task 3 |
| §8e Razor cutover + ViewModel deletion | Task 7 |
| §9 Topbar bell + ReminderCountProvider | Task 18 |
| §10 Verification gate | Task 20 |
| docs/models.md drift fix | Task 8 |
| Spec doc committed | Task 10 |

### Type consistency

| Symbol | Defined in | Used in |
|---|---|---|
| `RecurringTransactionListItemDto` | `reminder-status.ts` | `recurring-api.ts` (re-export), `RecurringTable`, `RecurringRowMenu`, `RecurringLayout`, `ReminderCountProvider` |
| `RecurringTransactionDetailDto` | `recurring-api.ts` | `RecurringEdit` |
| `RecurringFormValues` | `RecurringForm.tsx` | `RecurringCreate`, `RecurringEdit` |
| `RecurringPageCtx` | `RecurringCreate.tsx` | `RecurringEdit`, bridge components in `App.tsx` |
| `RecurringLayoutCtx` | `RecurringLayout.tsx` | `useRecurringLayoutCtx`, bridge components |
| `RECURRING_CONFIRM_URL` | `recurring-api.ts` | `RecurringConfirmDialog` |
| `RECURRING_DISMISS_URL` | `recurring-api.ts` | `RecurringDismissDialog` |
| `RECURRING_ARCHIVE_URL` | `recurring-api.ts` | `RecurringRowMenu` |
| `RECURRING_REACTIVATE_URL` | `recurring-api.ts` | `RecurringRowMenu` |
| `TryReactivateAsync(Guid)` | `IRecurringTransactionService` + service | `RecurringTransactionsApiController.Reactivate` |
| `TryDismissAsync(Guid, DateOnly?)` | `IRecurringTransactionService` + service | `RecurringTransactionsApiController.Dismiss` |
| `DismissRecurringTransactionRequest` | `RecurringTransactionApiDtos.cs` | `RecurringTransactionsApiController.Dismiss` |
| `RecurringTransactionPolicies.ValidateSchedule` | `RecurringTransactionPolicies.cs` | `RecurringTransactionService.TryCreateAsync`, `TryUpdateAsync` |

No mismatches found.
