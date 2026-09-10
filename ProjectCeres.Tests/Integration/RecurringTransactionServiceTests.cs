using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Tests.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for RecurringTransactionService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary  (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationParallel1")]
public class RecurringTransactionServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private RecurringTransactionService _service = null!;
    private AccountService _accountService = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);
        _service = new RecurringTransactionService(_fixture.Db, _accountService, new FakeCurrentUserAccessor(new Guid("00000000-0000-0000-0000-000000000001")), TimeProvider.System);

        // Create a reusable test account with no opening balance so any date is valid for ConfirmAsync.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            UserId        = new Guid("00000000-0000-0000-0000-000000000001"),
            Name          = $"Recurring Test Account {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        _fixture.Db.Accounts.Add(account);
        await _fixture.Db.SaveChangesAsync();
        _accountId = account.Id;
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<RecurringTransaction> CreateReminderAsync(
        string? name = null,
        Frequency frequency = Frequency.Monthly,
        DateOnly? nextDueDate = null,
        Guid? categoryId = null)
    {
        var result = await _service.TryCreateAsync(new CreateRecurringTransactionRequest(
            Name:              name ?? $"Reminder {Guid.NewGuid():N}",
            EstimatedAmount:   200m,
            AccountId:         _accountId,
            CategoryId:        categoryId ?? SalaryCategoryId,
            Frequency:         frequency.ToString(),
            DayOfPeriod:       null,
            NextDueDate:       nextDueDate ?? DateOnly.FromDateTime(DateTime.Today),
            ReminderBehaviour: ReminderBehaviour.RelativeToLastConfirmation.ToString()
        ));
        result.IsSuccess.Should().BeTrue("CreateReminderAsync helper must succeed");
        return result.Value!;
    }

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsReminderWithNavigationProperties()
    {
        var reminder = await CreateReminderAsync();

        var result = await _service.GetByIdAsync(reminder.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(reminder.Id);
        result.Account.Should().NotBeNull();
        result.Category.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // GetAllAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyActiveReminders_ByDefault()
    {
        var active   = await CreateReminderAsync(name: "Active Reminder");
        var inactive = await CreateReminderAsync(name: "Inactive Reminder");
        await _service.TryDeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync()).ToList();

        results.Should().Contain(r => r.Id == active.Id);
        results.Should().NotContain(r => r.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactive_ReturnsBothActiveAndInactive()
    {
        var active   = await CreateReminderAsync(name: "Active Reminder");
        var inactive = await CreateReminderAsync(name: "Inactive Reminder");
        await _service.TryDeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync(includeInactive: true)).ToList();

        results.Should().Contain(r => r.Id == active.Id);
        results.Should().Contain(r => r.Id == inactive.Id);
    }

    // -------------------------------------------------------------------------
    // SnapToCalendarDay — unit-level (Weekly + Biweekly)
    // -------------------------------------------------------------------------

    private static RecurringTransaction MakeReminder(
        Frequency frequency, ReminderBehaviour behaviour, int? dayOfPeriod, DateOnly nextDueDate) =>
        new RecurringTransaction
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(),
            Name = "Test", AccountId = Guid.NewGuid(), CategoryId = Guid.NewGuid(),
            Frequency = frequency, ReminderBehaviour = behaviour,
            DayOfPeriod = dayOfPeriod, NextDueDate = nextDueDate, IsActive = true,
        };

    private static DateOnly InvokeSnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
    {
        var method = typeof(RecurringTransactionService).GetMethod(
            "SnapToCalendarDay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (DateOnly)method.Invoke(null, [reminder, confirmDate])!;
    }

    [Fact]
    public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromEarlierInWeek()
    {
        // Confirm on Wednesday 2026-04-29 (Wed), target Monday (DayOfPeriod=1)
        var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
            nextDueDate: new DateOnly(2026, 4, 27));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 29)); // Wed
        result.Should().Be(new DateOnly(2026, 5, 4)); // Next Monday from Wed
    }

    [Fact]
    public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromTargetDay()
    {
        // Confirm on Monday 2026-04-27 (Mon), target Monday (DayOfPeriod=1)
        // Same-day: daysAhead = 0 → guard fires → daysAhead = 7 → next Monday
        var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
            nextDueDate: new DateOnly(2026, 4, 27));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 27));
        // daysAhead = (1 - 1 + 7) % 7 = 0 → same-day guard → daysAhead = 7 → 2026-05-04
        result.Should().Be(new DateOnly(2026, 5, 4));
    }

    [Fact]
    public void SnapToCalendarDay_Weekly_AdvancesToTargetWeekday_FromLaterInWeek()
    {
        // Confirm on Friday 2026-05-01, target Wednesday (DayOfPeriod=3)
        var reminder = MakeReminder(Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
            nextDueDate: new DateOnly(2026, 5, 1));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 1)); // Fri
        result.Should().Be(new DateOnly(2026, 5, 6)); // Next Wednesday from Fri
    }

    [Fact]
    public void SnapToCalendarDay_Biweekly_AdvancesAtLeast8Days_FromTargetDay()
    {
        // Confirm on Wednesday 2026-04-29, target Wednesday (DayOfPeriod=3)
        var reminder = MakeReminder(Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
            nextDueDate: new DateOnly(2026, 4, 29));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 29));
        result.Should().Be(new DateOnly(2026, 5, 13)); // daysAhead=0 → 7; biweekly: +7 → 14 days
    }

    [Fact]
    public void SnapToCalendarDay_Biweekly_TargetMidweek()
    {
        // Confirm on Tuesday 2026-04-28, target Wednesday (DayOfPeriod=3)
        // daysAhead = (Wed=3 - Tue=2 + 7) % 7 = 1; 1 < 8 → add 7 → 8 days
        var reminder = MakeReminder(Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 3,
            nextDueDate: new DateOnly(2026, 4, 28));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 4, 28));
        result.Should().Be(new DateOnly(2026, 5, 6)); // Tue + 8 days = Wed May 6
    }

    [Fact]
    public void SnapToCalendarDay_Monthly_AnchorIs1st_ConfirmOnAnchor_AdvancesToNextMonthAnchor()
    {
        // Reproduces the production bug: monthly rent on the 1st, confirmed on May 1st,
        // must advance to June 1st (not July 1st).
        var reminder = MakeReminder(Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
            nextDueDate: new DateOnly(2026, 5, 1));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 1));
        result.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void SnapToCalendarDay_Monthly_AnchorIs15th_ConfirmEarlyInMonth_AdvancesToNextMonthAnchor()
    {
        // Confirm before the anchor day — next due is next month's anchor.
        var reminder = MakeReminder(Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15,
            nextDueDate: new DateOnly(2026, 5, 15));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 10));
        result.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void SnapToCalendarDay_Monthly_AnchorIs15th_ConfirmLateInMonth_AdvancesToNextMonthAnchor()
    {
        // Confirm late (after the anchor) — next due is still next month's anchor, never month-after-next.
        var reminder = MakeReminder(Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15,
            nextDueDate: new DateOnly(2026, 5, 15));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 25));
        result.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void SnapToCalendarDay_Monthly_AnchorIs1st_ConfirmLateInMonth_AdvancesToNextMonthAnchor()
    {
        // Confirm a 1st-of-month reminder mid-month — next due is the 1st of next month, not the month after.
        var reminder = MakeReminder(Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1,
            nextDueDate: new DateOnly(2026, 5, 1));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 5, 15));
        result.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void SnapToCalendarDay_Monthly_AnchorIs31st_FebruaryClampsToLastDay()
    {
        // 31st-of-month reminder confirmed in January → February has 28 days in 2026, so clamp.
        var reminder = MakeReminder(Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 31,
            nextDueDate: new DateOnly(2026, 1, 31));
        var result = InvokeSnapToCalendarDay(reminder, confirmDate: new DateOnly(2026, 1, 31));
        result.Should().Be(new DateOnly(2026, 2, 28));
    }

    // -------------------------------------------------------------------------
    // GetUpcomingAsync (6.2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUpcomingAsync_ReturnsRemindersWithinWindow_ExcludesOutside()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Due today — included
        var dueToday = await CreateReminderAsync(name: "Due Today", nextDueDate: today);
        // Due in 5 days — included
        var dueSoon  = await CreateReminderAsync(name: "Due Soon", nextDueDate: today.AddDays(5));
        // Due in 35 days — excluded
        var dueLater = await CreateReminderAsync(name: "Due Later", nextDueDate: today.AddDays(35));

        var results = (await _service.GetUpcomingAsync(withinDays: 30)).ToList();

        results.Should().Contain(r => r.Id == dueToday.Id);
        results.Should().Contain(r => r.Id == dueSoon.Id);
        results.Should().NotContain(r => r.Id == dueLater.Id);
    }

    [Fact]
    public async Task GetUpcomingAsync_IncludesOverdueReminders()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Overdue (due 5 days ago) — must be included even with withinDays=0
        var overdue = await CreateReminderAsync(name: "Overdue Reminder", nextDueDate: today.AddDays(-5));

        var results = (await _service.GetUpcomingAsync(withinDays: 0)).ToList();

        results.Should().Contain(r => r.Id == overdue.Id);
    }

    // -------------------------------------------------------------------------
    // TryReactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryReactivateAsync_returns_ok_for_archived_reminder()
    {
        var reminder = await CreateReminderAsync();
        await _service.TryDeactivateAsync(reminder.Id);

        var result = await _service.TryReactivateAsync(reminder.Id);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TryReactivateAsync_is_idempotent_for_active_reminder()
    {
        var reminder = await CreateReminderAsync();

        var result = await _service.TryReactivateAsync(reminder.Id);

        result.IsSuccess.Should().BeTrue();
        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TryReactivateAsync_returns_fail_for_unknown_id()
    {
        var result = await _service.TryReactivateAsync(Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be("NOT_FOUND");
    }
}
