using FluentAssertions;
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
[Collection("IntegrationTests")]
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
        _accountService = new AccountService(_fixture.Db);
        _service = new RecurringTransactionService(_fixture.Db, _accountService);

        // Create a reusable test account with no opening balance so any date is valid for ConfirmAsync.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
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

    private Task<RecurringTransaction> CreateReminderAsync(
        string? name = null,
        Frequency frequency = Frequency.Monthly,
        DateOnly? nextDueDate = null,
        Guid? categoryId = null) =>
        _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name            = name ?? $"Reminder {Guid.NewGuid():N}",
            EstimatedAmount = 200m,
            AccountId       = _accountId,
            CategoryId      = categoryId ?? SalaryCategoryId,
            Frequency       = frequency,
            DayOfPeriod     = null,
            NextDueDate     = nextDueDate ?? DateOnly.FromDateTime(DateTime.Today)
        });

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsReminder()
    {
        var dueDate = new DateOnly(2026, 5, 1);

        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name            = "Monthly Rent",
            EstimatedAmount = 800m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Monthly,
            DayOfPeriod     = 1,
            NextDueDate     = dueDate
        });

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Name.Should().Be("Monthly Rent");
        reloaded.EstimatedAmount.Should().Be(800m);
        reloaded.AccountId.Should().Be(_accountId);
        reloaded.CategoryId.Should().Be(HousingCategoryId);
        reloaded.Frequency.Should().Be(Frequency.Monthly);
        reloaded.DayOfPeriod.Should().Be(1);
        reloaded.NextDueDate.Should().Be(dueDate);
        reloaded.IsActive.Should().BeTrue();
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
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync()).ToList();

        results.Should().Contain(r => r.Id == active.Id);
        results.Should().NotContain(r => r.Id == inactive.Id);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactive_ReturnsBothActiveAndInactive()
    {
        var active   = await CreateReminderAsync(name: "Active Reminder");
        var inactive = await CreateReminderAsync(name: "Inactive Reminder");
        await _service.DeactivateAsync(inactive.Id);

        var results = (await _service.GetAllAsync(includeInactive: true)).ToList();

        results.Should().Contain(r => r.Id == active.Id);
        results.Should().Contain(r => r.Id == inactive.Id);
    }

    // -------------------------------------------------------------------------
    // UpdateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ChangesAllFields()
    {
        var reminder    = await CreateReminderAsync(name: "Old Name");
        var newDueDate  = new DateOnly(2026, 8, 15);

        await _service.UpdateAsync(new RecurringTransactionEditViewModel
        {
            Id              = reminder.Id,
            Name            = "New Name",
            EstimatedAmount = 999m,
            AccountId       = _accountId,
            CategoryId      = HousingCategoryId,
            Frequency       = Frequency.Annual,
            DayOfPeriod     = 15,
            NextDueDate     = newDueDate
        });

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.Name.Should().Be("New Name");
        reloaded.EstimatedAmount.Should().Be(999m);
        reloaded.CategoryId.Should().Be(HousingCategoryId);
        reloaded.Frequency.Should().Be(Frequency.Annual);
        reloaded.DayOfPeriod.Should().Be(15);
        reloaded.NextDueDate.Should().Be(newDueDate);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.UpdateAsync(new RecurringTransactionEditViewModel
        {
            Id              = Guid.NewGuid(),
            Name            = "Ghost",
            EstimatedAmount = 0m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = DateOnly.FromDateTime(DateTime.Today)
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // DeactivateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var reminder = await CreateReminderAsync();

        await _service.DeactivateAsync(reminder.Id);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DeactivateAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ConfirmAsync — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_CreatesTransaction_WithCorrectFields()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await CreateReminderAsync(frequency: Frequency.Monthly, nextDueDate: dueDate);

        var confirmDate = new DateOnly(2026, 5, 3);
        var tx = await _service.ConfirmAsync(reminder.Id, confirmDate, amount: 250m, description: "May salary");

        var reloaded = await _fixture.Db.Transactions.FindAsync(tx.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Date.Should().Be(confirmDate);
        reloaded.Amount.Should().Be(250m);
        reloaded.Description.Should().Be("May salary");
        reloaded.AccountId.Should().Be(_accountId);
        reloaded.CategoryId.Should().Be(SalaryCategoryId);
    }

    [Fact]
    public async Task ConfirmAsync_UsesReminderName_WhenDescriptionIsNull()
    {
        var reminder = await CreateReminderAsync(name: "Monthly Salary");

        var tx = await _service.ConfirmAsync(reminder.Id, DateOnly.FromDateTime(DateTime.Today), amount: 100m, description: null);

        var reloaded = await _fixture.Db.Transactions.FindAsync(tx.Id);
        reloaded!.Description.Should().Be("Monthly Salary");
    }

    // -------------------------------------------------------------------------
    // ConfirmAsync — NextDueDate advancement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_AdvancesNextDueDate_Monthly()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await CreateReminderAsync(frequency: Frequency.Monthly, nextDueDate: dueDate);

        await _service.ConfirmAsync(reminder.Id, dueDate, amount: 100m, description: null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public async Task ConfirmAsync_AdvancesNextDueDate_Weekly()
    {
        var dueDate  = new DateOnly(2026, 5, 4);  // Monday
        var reminder = await CreateReminderAsync(frequency: Frequency.Weekly, nextDueDate: dueDate);

        await _service.ConfirmAsync(reminder.Id, dueDate, amount: 50m, description: null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 5, 11));
    }

    [Fact]
    public async Task ConfirmAsync_AdvancesNextDueDate_Biweekly()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await CreateReminderAsync(frequency: Frequency.Biweekly, nextDueDate: dueDate);

        await _service.ConfirmAsync(reminder.Id, dueDate, amount: 50m, description: null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 5, 15));
    }

    [Fact]
    public async Task ConfirmAsync_AdvancesNextDueDate_Annual()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await CreateReminderAsync(frequency: Frequency.Annual, nextDueDate: dueDate);

        await _service.ConfirmAsync(reminder.Id, dueDate, amount: 50m, description: null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2027, 5, 1));
    }

    // -------------------------------------------------------------------------
    // ConfirmAsync — opening balance date guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_ThrowsWhenDateIsBeforeAccountOpeningBalance()
    {
        var openingDate = new DateOnly(2026, 3, 1);
        var account = await _accountService.CreateAsync(new AccountCreateViewModel
        {
            Name               = $"Account {Guid.NewGuid():N}",
            AccountTypeId      = 1,
            CurrencyId         = 1,
            OpeningBalance     = 500m,
            OpeningBalanceDate = openingDate
        });

        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name            = "Guarded Reminder",
            EstimatedAmount = 100m,
            AccountId       = account.Id,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = new DateOnly(2026, 2, 1)
        });

        var act = async () => await _service.ConfirmAsync(
            reminder.Id,
            date: new DateOnly(2026, 2, 1),
            amount: 100m,
            description: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opening balance date*");
    }

    [Fact]
    public async Task ConfirmAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.ConfirmAsync(
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today),
            amount: 100m,
            description: null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // DismissAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DismissAsync_AdvancesNextDueDate_WithoutCreatingTransaction()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await CreateReminderAsync(frequency: Frequency.Monthly, nextDueDate: dueDate);
        var txCountBefore = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);

        await _service.DismissAsync(reminder.Id);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 6, 1));

        var txCountAfter = _fixture.Db.Transactions.Count(t => t.AccountId == _accountId);
        txCountAfter.Should().Be(txCountBefore, "DismissAsync must not create a transaction");
    }

    [Fact]
    public async Task DismissAsync_ThrowsWhenNotFound()
    {
        var act = async () => await _service.DismissAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ReminderBehaviour — SnapToCalendarDay
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_SnapToCalendarDay_OnTime_AdvancesToDayOfPeriodNextMonth()
    {
        // Confirmed on the 5th, DayOfPeriod = 15 → next due = 15th of the following month
        var dueDate  = new DateOnly(2026, 5, 5);
        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name              = "Snap On Time",
            EstimatedAmount   = 100m,
            AccountId         = _accountId,
            CategoryId        = SalaryCategoryId,
            Frequency         = Frequency.Monthly,
            DayOfPeriod       = 15,
            NextDueDate       = dueDate,
            ReminderBehaviour = ReminderBehaviour.SnapToCalendarDay
        });

        await _service.ConfirmAsync(reminder.Id, dueDate, 100m, null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task ConfirmAsync_SnapToCalendarDay_ConfirmedLate_SkipsForwardToFollowingMonth()
    {
        // Confirmed on the 20th, DayOfPeriod = 15 → skips to 15th of month after next
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name              = "Snap Late",
            EstimatedAmount   = 100m,
            AccountId         = _accountId,
            CategoryId        = SalaryCategoryId,
            Frequency         = Frequency.Monthly,
            DayOfPeriod       = 15,
            NextDueDate       = dueDate,
            ReminderBehaviour = ReminderBehaviour.SnapToCalendarDay
        });

        // Confirm late — on the 20th (past DayOfPeriod = 15)
        await _service.ConfirmAsync(reminder.Id, new DateOnly(2026, 5, 20), 100m, null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 7, 15));
    }

    // -------------------------------------------------------------------------
    // ReminderBehaviour — RelativeToLastConfirmation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_RelativeToLastConfirmation_Monthly_AdvancesFromConfirmDate()
    {
        // Confirmed on the 20th (monthly) → next due = 20th of next month
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name              = "Relative Monthly",
            EstimatedAmount   = 100m,
            AccountId         = _accountId,
            CategoryId        = SalaryCategoryId,
            Frequency         = Frequency.Monthly,
            DayOfPeriod       = null,
            NextDueDate       = dueDate,
            ReminderBehaviour = ReminderBehaviour.RelativeToLastConfirmation
        });

        await _service.ConfirmAsync(reminder.Id, new DateOnly(2026, 5, 20), 100m, null);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(new DateOnly(2026, 6, 20));
    }

    // -------------------------------------------------------------------------
    // ReminderBehaviour — ManualDate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_ManualDate_WithoutNextDueDate_Throws()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name              = "Manual No Date",
            EstimatedAmount   = 100m,
            AccountId         = _accountId,
            CategoryId        = SalaryCategoryId,
            Frequency         = Frequency.Monthly,
            DayOfPeriod       = null,
            NextDueDate       = dueDate,
            ReminderBehaviour = ReminderBehaviour.ManualDate
        });

        var act = async () => await _service.ConfirmAsync(reminder.Id, dueDate, 100m, null, nextDueDate: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*next due date*");
    }

    [Fact]
    public async Task ConfirmAsync_ManualDate_WithNextDueDate_SetsExactDate()
    {
        var dueDate  = new DateOnly(2026, 5, 1);
        var reminder = await _service.CreateAsync(new RecurringTransactionCreateViewModel
        {
            Name              = "Manual With Date",
            EstimatedAmount   = 100m,
            AccountId         = _accountId,
            CategoryId        = SalaryCategoryId,
            Frequency         = Frequency.Monthly,
            DayOfPeriod       = null,
            NextDueDate       = dueDate,
            ReminderBehaviour = ReminderBehaviour.ManualDate
        });

        var manualNext = new DateOnly(2026, 8, 10);
        await _service.ConfirmAsync(reminder.Id, dueDate, 100m, null, nextDueDate: manualNext);

        var reloaded = await _fixture.Db.RecurringTransactions.FindAsync(reminder.Id);
        reloaded!.NextDueDate.Should().Be(manualNext);
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
}
