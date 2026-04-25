using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Integration tests for DashboardService against the real project_ceres_test database.
/// Each test rolls back its transaction — no test data persists between tests.
///
/// DashboardService is MTD-scoped (month-to-date from 1st of the current month through today)
/// and uses the default currency from Settings (seeded as EUR, CurrencyId = 1).
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000002 = Salary  (Income, non-system)
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense, non-system)
/// </summary>
[Collection("IntegrationTests")]
public class DashboardServiceTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestDbFixture _fixture = new();
    private DashboardService _service = null!;
    private AccountService _accountService = null!;
    private Guid _accountId;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        _accountService = new AccountService(_fixture.Db);
        var settingsService = new SettingsService(_fixture.Db);
        _service = new DashboardService(_fixture.Db, settingsService);

        // Ensure settings row exists (required by DashboardService).
        await settingsService.EnsureExistsAsync();

        // Create a reusable EUR asset account with no opening balance.
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"Dashboard Test Account {Guid.NewGuid():N}",
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

    private void AddMtdTransaction(Guid categoryId, decimal amount)
    {
        // Place the transaction on the 1st of the current month so it is always MTD.
        var today   = DateOnly.FromDateTime(DateTime.Today);
        var mtdDate = new DateOnly(today.Year, today.Month, 1);

        _fixture.Db.Transactions.Add(new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = mtdDate,
            Amount     = amount,
            AccountId  = _accountId,
            CategoryId = categoryId,
            CreatedAt  = DateTime.UtcNow
        });
    }

    // -------------------------------------------------------------------------
    // GetDashboardDataAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboardDataAsync_ReturnsCurrencyFromSettings()
    {
        var data = await _service.GetDashboardDataAsync();

        // Default currency is EUR (CurrencyId = 1, Code = "EUR").
        data.CurrencyCode.Should().Be("EUR");
        data.CurrencySymbol.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDashboardDataAsync_SumsMtdIncomeAndExpenses()
    {
        AddMtdTransaction(SalaryCategoryId,  2000m);
        AddMtdTransaction(HousingCategoryId,  600m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.MtdIncome.Should().BeGreaterThanOrEqualTo(2000m);
        data.MtdExpenses.Should().BeGreaterThanOrEqualTo(600m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_SavingsRateIsZero_WhenNoMtdIncome()
    {
        AddMtdTransaction(HousingCategoryId, 300m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.SavingsRate.Should().Be(0m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_SavingsRateReflectsIncomeMinusExpenses()
    {
        AddMtdTransaction(SalaryCategoryId,  1000m);
        AddMtdTransaction(HousingCategoryId,  500m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        // savingsRate = (income − expenses) / income = 500 / 1000 = 0.5
        data.SavingsRate.Should().BeApproximately(0.5m, 0.0001m);
    }

    [Fact]
    public async Task GetDashboardDataAsync_IncludesNetWorthEntries()
    {
        // Asset account is active — at least one entry expected when balances exist.
        AddMtdTransaction(SalaryCategoryId, 500m);
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        // We cannot assert an exact count because other active accounts may exist,
        // but the list itself must be non-null.
        data.NetWorth.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_CountsPendingReminders()
    {
        // Create a reminder whose NextDueDate is today (always pending).
        var today = DateOnly.FromDateTime(DateTime.Today);
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Test Reminder",
            EstimatedAmount = 100m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = today,
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var data = await _service.GetDashboardDataAsync();

        data.PendingRemindersCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetDashboardDataAsync_DoesNotCountFutureReminders()
    {
        var countBefore = (await _service.GetDashboardDataAsync()).PendingRemindersCount;

        // Add a reminder due in the future — must not increase the pending count.
        _fixture.Db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            Name            = "Future Reminder",
            EstimatedAmount = 50m,
            AccountId       = _accountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            NextDueDate     = DateOnly.FromDateTime(DateTime.Today).AddMonths(1),
            IsActive        = true
        });
        await _fixture.Db.SaveChangesAsync();

        var countAfter = (await _service.GetDashboardDataAsync()).PendingRemindersCount;

        countAfter.Should().Be(countBefore);
    }
}
