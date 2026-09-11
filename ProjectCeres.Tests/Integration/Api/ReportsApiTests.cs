using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel1")]
public class ReportsApiTests : IntegrationTestBase<Bucket1Factory>, IAsyncLifetime
{
    private static readonly Guid CheckingAccountId = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008"); // Expense
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002"); // Income

    private readonly Bucket1Factory _factory;
    private readonly HttpClient _client;
    private readonly Guid _intruderUserId = Guid.NewGuid();
    private readonly List<Guid> _intruderTransactionIds      = [];
    private readonly List<Guid> _intruderTransferIds         = [];
    private readonly List<Guid> _intruderLiabilityPaymentIds = [];
    private readonly List<Guid> _intruderBudgetIds           = [];
    private readonly List<Guid> _intruderCategoryBudgetIds   = [];
    private readonly List<Guid> _intruderAccountIds          = [];

    public ReportsApiTests(Bucket1Factory factory, Bucket1Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_intruderTransactionIds.Count > 0)      await db.Transactions.Where(t => _intruderTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_intruderTransferIds.Count > 0)         await db.Transfers.Where(t => _intruderTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_intruderLiabilityPaymentIds.Count > 0) await db.LiabilityPayments.Where(p => _intruderLiabilityPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_intruderBudgetIds.Count > 0)           await db.Budgets.Where(b => _intruderBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
        if (_intruderCategoryBudgetIds.Count > 0)   await db.CategoryBudgets.Where(cb => _intruderCategoryBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
        if (_intruderAccountIds.Count > 0)          await db.Accounts.Where(a => _intruderAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Intruder seeding helpers — seeds rows owned by another user that will
    // poison every report unless the chokepoint filters them out.
    // -------------------------------------------------------------------------

    private const decimal IntruderMarkerAmount = 12345.67m; // unique value to detect leakage by content

    /// <summary>
    /// Seeds an intruder asset account with an opening-balance transaction and one
    /// expense + one income transaction. This populates net-worth, income/expense,
    /// expense-breakdown, transaction-history, largest-expenses, monthly-cash-flow,
    /// and net-worth-over-time reports — if any of them leaks the intruder, the
    /// IntruderMarkerAmount value will appear in the response.
    /// </summary>
    private async Task<Guid> SeedIntruderAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            UserId        = _intruderUserId,
            Name          = $"intruder-{Guid.NewGuid():N}",
            AccountTypeId = 1, // Asset
            CurrencyId    = 1, // EUR
            IsActive      = true,
        };
        db.Accounts.Add(account);
        _intruderAccountIds.Add(account.Id);

        // Opening balance — feeds NetWorth + NetWorthOverTime.
        var ob = new Transaction
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            Date        = new DateOnly(2026, 1, 1),
            Amount      = IntruderMarkerAmount,
            AccountId   = account.Id,
            CategoryId  = new Guid("20000000-0000-0000-0000-000000000001"), // Opening Balance system category
            Description = "intruder-opening",
            CreatedAt   = DateTime.UtcNow,
        };
        db.Transactions.Add(ob);
        _intruderTransactionIds.Add(ob.Id);

        // Expense — feeds IncomeExpense, ExpenseBreakdown, TransactionHistory, LargestExpenses, MonthlyCashFlow.
        var expense = new Transaction
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            Date        = new DateOnly(2026, 4, 10),
            Amount      = IntruderMarkerAmount,
            AccountId   = account.Id,
            CategoryId  = HousingCategoryId,
            Description = "intruder-expense",
            CreatedAt   = DateTime.UtcNow,
        };
        db.Transactions.Add(expense);
        _intruderTransactionIds.Add(expense.Id);

        // Income — feeds IncomeExpense, MonthlyCashFlow, TransactionHistory.
        var income = new Transaction
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            Date        = new DateOnly(2026, 4, 11),
            Amount      = IntruderMarkerAmount,
            AccountId   = account.Id,
            CategoryId  = SalaryCategoryId,
            Description = "intruder-income",
            CreatedAt   = DateTime.UtcNow,
        };
        db.Transactions.Add(income);
        _intruderTransactionIds.Add(income.Id);

        // CategoryBudget — feeds BudgetVsActual.
        var cb = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = IntruderMarkerAmount,
            IsActive    = true,
        };
        db.CategoryBudgets.Add(cb);
        _intruderCategoryBudgetIds.Add(cb.Id);

        await db.SaveChangesAsync();
        return account.Id;
    }

    private static string Wide(DateOnly start = default, DateOnly end = default) =>
        $"?from={(start == default ? new DateOnly(2026, 1, 1) : start):yyyy-MM-dd}" +
        $"&to={(end == default ? new DateOnly(2026, 12, 31) : end):yyyy-MM-dd}";

    private static async Task<string> GetBodyAsync(HttpResponseMessage res) =>
        await res.Content.ReadAsStringAsync();

    // -------------------------------------------------------------------------
    // Happy paths: each endpoint returns 200 with a recognisable JSON shape.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NetWorth_returns_200()
    {
        var res = await _client.GetAsync("/api/reports/net-worth");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows.Should().NotBeNull();
        // Per-currency entries — at least one of the seeded currencies should be present.
        rows!.Should().OnlyContain(r => r.GetProperty("currencyCode").GetString() != null);
    }

    [Fact]
    public async Task IncomeExpense_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/income-expense{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("totalIncome").GetDecimal().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExpenseBreakdown_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/expense-breakdown{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.TryGetProperty("categories", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TransactionHistory_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/transaction-history{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows.Should().NotBeNull();
    }

    [Fact]
    public async Task BudgetVsActual_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/budget-vs-actual{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LargestExpenses_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/largest-expenses{Wide()}&limit=5");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows.Should().NotBeNull();
    }

    [Fact]
    public async Task MonthlyCashFlow_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/monthly-cash-flow{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NetWorthOverTime_returns_200()
    {
        var res = await _client.GetAsync($"/api/reports/net-worth-over-time{Wide()}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // Intruder isolation: one test per endpoint.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NetWorth_excludes_intruder_account()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync("/api/reports/net-worth");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task IncomeExpense_excludes_intruder_transactions()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/income-expense{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ExpenseBreakdown_excludes_intruder_transactions()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/expense-breakdown{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TransactionHistory_excludes_intruder_transactions()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/transaction-history{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        body.Should().NotContain("intruder-expense");
        body.Should().NotContain("intruder-income");
    }

    [Fact]
    public async Task BudgetVsActual_excludes_intruder_budget()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/budget-vs-actual{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LargestExpenses_excludes_intruder_transactions()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/largest-expenses{Wide()}&limit=50");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        body.Should().NotContain("intruder-expense");
    }

    [Fact]
    public async Task MonthlyCashFlow_excludes_intruder_transactions()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/monthly-cash-flow{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task NetWorthOverTime_excludes_intruder_account()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/net-worth-over-time{Wide()}");
        var body = await GetBodyAsync(res);
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // -------------------------------------------------------------------------
    // CSV format: each endpoint streams text/csv with Content-Disposition,
    // and the CSV must not contain intruder data either.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NetWorth_csv_streams_with_disposition_and_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync("/api/reports/net-worth?format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        res.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var body = await res.Content.ReadAsStringAsync();
        body.Should().StartWith("Currency,Assets,Liabilities,Net Worth");
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task IncomeExpense_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/income-expense{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ExpenseBreakdown_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/expense-breakdown{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TransactionHistory_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/transaction-history{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain("intruder-expense");
        body.Should().NotContain("intruder-income");
    }

    [Fact]
    public async Task BudgetVsActual_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/budget-vs-actual{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LargestExpenses_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/largest-expenses{Wide()}&limit=50&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain("intruder-expense");
    }

    [Fact]
    public async Task MonthlyCashFlow_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/monthly-cash-flow{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task NetWorthOverTime_csv_excludes_intruder()
    {
        await SeedIntruderAsync();
        var res = await _client.GetAsync($"/api/reports/net-worth-over-time{Wide()}&format=csv");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(IntruderMarkerAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
