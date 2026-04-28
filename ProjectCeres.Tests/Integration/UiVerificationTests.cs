using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WAF tests covering Stage 5 and Stage 6 UI/UX checklist items:
///
/// — Stage 5.1: Account Edit for a Liability account renders the LiabilityRepaymentType
///   select and the repayment type description hint. InterestRate field is present in HTML
///   but hidden via JS when RepaymentType != Amortising (JS show/hide not testable here).
///
/// — Stage 5.2: Amortising account Ledger page renders the Payoff Projection section with
///   payoff date, interest cost, and the "what if" monthly payment input.
///
/// — Stage 6.1: Recurring Transaction Create renders the ReminderBehaviour select,
///   the EstimatedAmount field, and the DayOfPeriod field. JS-driven hide on ManualDate
///   is not testable via WAF.
///
/// — Stage 6.2: Upcoming Payments page renders a table; due-today rows contain the
///   "Due today" badge; the navbar data-upcoming-count attribute is present.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset, 2 = Liability
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense)
/// </summary>
[Collection("IntegrationTests")]
public class UiVerificationTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededReminderIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public UiVerificationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_seededTransactionIds.Count > 0)
            await db.Transactions
                .Where(t => _seededTransactionIds.Contains(t.Id))
                .ExecuteDeleteAsync();

        if (_seededReminderIds.Count > 0)
            await db.RecurringTransactions
                .Where(r => _seededReminderIds.Contains(r.Id))
                .ExecuteDeleteAsync();

        if (_seededAccountIds.Count > 0)
            await db.Accounts
                .Where(a => _seededAccountIds.Contains(a.Id))
                .ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedLiabilityAccountAsync(
        string repaymentType = "Amortising",
        decimal? interestRate = 0.035m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id                     = Guid.NewGuid(),
            Name                   = $"WAF Liability {Guid.NewGuid():N}",
            AccountTypeId          = 2,
            CurrencyId             = 1,
            IsActive               = true,
            LiabilityRepaymentType = repaymentType,
            InterestRate           = interestRate
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);
        return account.Id;
    }

    private async Task<Guid> SeedAssetAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"WAF Asset {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);
        return account.Id;
    }

    private async Task<Guid> SeedReminderDueTodayAsync(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reminder = new RecurringTransaction
        {
            Id                = Guid.NewGuid(),
            Name              = $"WAF Reminder {Guid.NewGuid():N}",
            AccountId         = accountId,
            CategoryId        = HousingCategoryId,
            Frequency         = Frequency.Monthly,
            ReminderBehaviour = ReminderBehaviour.SnapToCalendarDay,
            NextDueDate       = DateOnly.FromDateTime(DateTime.Today),
            EstimatedAmount   = 100m,
            IsActive          = true
        };
        db.RecurringTransactions.Add(reminder);
        await db.SaveChangesAsync();
        _seededReminderIds.Add(reminder.Id);
        return reminder.Id;
    }

    // -------------------------------------------------------------------------
    // Stage 5.1 — Account Edit: LiabilityRepaymentType select + hint text
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AccountEdit_LiabilityAccount_RendersRepaymentTypeSelectAndHint()
    {
        var accountId = await SeedLiabilityAccountAsync("FullMonthly", null);

        var response = await _client.GetAsync($"/Accounts/Edit/{accountId}");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("LiabilityRepaymentType",
            because: "the repayment type select must be rendered for liability accounts");
        body.Should().Contain("Amortising",
            because: "the Amortising option must be present in the select");
        body.Should().Contain("Full Monthly",
            because: "the FullMonthly option must be present in the select");
        body.Should().Contain("Full Monthly",
            because: "the repayment type description hint must appear");
        body.Should().Contain("loans and mortgages",
            because: "the hint text explaining Amortising must be rendered");
    }

    [Fact]
    public async Task AccountEdit_LiabilityAccount_RendersInterestRateField()
    {
        var accountId = await SeedLiabilityAccountAsync("Amortising", 0.035m);

        var response = await _client.GetAsync($"/Accounts/Edit/{accountId}");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("InterestRate",
            because: "the interest rate input must be in the HTML for Amortising accounts");
    }

    private async Task SeedTransactionAsync(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var txn = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(2025, 1, 15),
            Amount     = 200m,
            AccountId  = accountId,
            CategoryId = HousingCategoryId,
            IsCleared  = false,
            CreatedAt  = DateTime.UtcNow
        };
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        _seededTransactionIds.Add(txn.Id);
    }

    // -------------------------------------------------------------------------
    // Stage 5.2 — Account Ledger: projection panel data and what-if input
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AccountLedger_AmorisingAccount_RendersProjectionPanelFormOnGet()
    {
        // A transaction is needed so balance > 0 — the projection panel only renders
        // when the account has a positive outstanding balance.
        var accountId = await SeedLiabilityAccountAsync("Amortising", 0.035m);
        await SeedTransactionAsync(accountId);

        var response = await _client.GetAsync($"/Accounts/Ledger/{accountId}");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Payoff Projection",
            because: "the projection panel heading must render for Amortising accounts with balance > 0");
        body.Should().Contain("Monthly payment amount",
            because: "the what-if monthly payment input must be rendered");
        body.Should().Contain("Calculate",
            because: "the Calculate submit button must be rendered");
    }

    [Fact]
    public async Task AccountLedger_AmorisingAccount_PostWithMonthlyPayment_RendersProjectionResults()
    {
        var accountId = await SeedLiabilityAccountAsync("Amortising", 0.035m);
        await SeedTransactionAsync(accountId);

        // GET first to obtain anti-forgery token
        var getResponse = await _client.GetAsync($"/Accounts/Ledger/{accountId}");
        var page        = await getResponse.Content.ReadAsStringAsync();
        var token       = System.Text.RegularExpressions.Regex.Match(
            page, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""")
            .Groups[1].Value;

        var form = new Dictionary<string, string>
        {
            ["monthlyPayment"]             = "100",
            ["__RequestVerificationToken"] = token
        };

        var response = await _client.PostAsync(
            $"/Accounts/Ledger/{accountId}",
            new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Total interest cost",
            because: "the projection results must include total interest cost after POST");
        body.Should().Contain("Estimated payoff",
            because: "the projected payoff date must appear in the results");
    }

    // -------------------------------------------------------------------------
    // Stage 6.1 — Recurring Transaction Create: fields present
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecurringTransactionCreate_RendersReminderBehaviourSelectAndEstimatedAmount()
    {
        var response = await _client.GetAsync("/RecurringTransactions/Create");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("ReminderBehaviour",
            because: "the ReminderBehaviour select must be rendered");
        body.Should().Contain("Snap to Calendar Day",
            because: "the SnapToCalendarDay option must be present");
        body.Should().Contain("Manual Date",
            because: "the ManualDate option must be present");
        body.Should().Contain("EstimatedAmount",
            because: "the EstimatedAmount field must be rendered");
        body.Should().Contain("DayOfPeriod",
            because: "the DayOfPeriod field must be present in the HTML (JS hides it for ManualDate)");
    }

    // -------------------------------------------------------------------------
    // Stage 6.2 — Upcoming Payments: table, due-today badge, navbar count attr
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpcomingPayments_WithDueTodayReminder_RendersDueTodayBadge()
    {
        var accountId = await SeedAssetAccountAsync();
        await SeedReminderDueTodayAsync(accountId);

        var response = await _client.GetAsync("/RecurringTransactions/Upcoming");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("data-table",
            because: "the upcoming payments table must use the data-table class");
        body.Should().Contain("Due today",
            because: "a reminder due today must show the Due today badge");
        body.Should().Contain("badge-warning",
            because: "the due-today badge must use the amber badge-warning class");
    }

    [Fact]
    public async Task AnyPage_NavbarRoot_RendersDataUpcomingCountAttribute()
    {
        var response = await _client.GetAsync("/RecurringTransactions/Upcoming");
        var body     = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("data-upcoming-count",
            because: "the navbar root must render data-upcoming-count for the React bell badge");
    }
}
