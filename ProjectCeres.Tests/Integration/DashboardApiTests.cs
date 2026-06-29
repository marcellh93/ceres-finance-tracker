using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class DashboardApiTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetCategoryBudgets_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/category-budgets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        // Each element must have spent, limit, percentUsed fields
        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("categoryName", out _).Should().BeTrue();
            item.TryGetProperty("currencyCode", out _).Should().BeTrue();
            item.TryGetProperty("spent", out _).Should().BeTrue();
            item.TryGetProperty("limit", out _).Should().BeTrue();
            item.TryGetProperty("percentUsed", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetGoalBudgets_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/goal-budgets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("goalType", out _).Should().BeTrue();
            item.TryGetProperty("amountProgress", out _).Should().BeTrue();
            item.TryGetProperty("targetAmount", out _).Should().BeTrue();
            item.TryGetProperty("percentUsed", out _).Should().BeTrue();
        }
    }

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

    [Fact]
    public async Task GetHealth_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Object);

        // All 15 documented fields must be present (values may be null).
        var requiredFields = new[]
        {
            "availableToday", "safeToSpend", "imminentBills", "laterBills",
            "budgetReserve", "runwayMonths", "avgMonthlyExpense",
            "currentMonthIncome", "rollingAverageIncome", "incomeDeltaPercent",
            "budgetBurnRate", "budgetSpentMtd", "budgetTotalLimit",
            "currencyCode", "currencySymbol",
        };
        foreach (var field in requiredFields)
        {
            body.TryGetProperty(field, out _).Should().BeTrue($"field {field} must be present");
        }
    }

    [Fact]
    public async Task GetHealth_CurrencyCodeAndSymbol_AreNonNullStrings()
    {
        var response = await _client.GetAsync("/api/dashboard/health");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("currencyCode").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("currencySymbol").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("currencyCode").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("currencySymbol").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSummary_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Object);

        body.TryGetProperty("netWorth", out var netWorth).Should().BeTrue();
        netWorth.ValueKind.Should().Be(JsonValueKind.Array);

        body.TryGetProperty("mtd", out var mtd).Should().BeTrue();
        mtd.ValueKind.Should().Be(JsonValueKind.Object);

        body.TryGetProperty("remindersDueCount", out var reminders).Should().BeTrue();
        reminders.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task GetSummary_MtdHasIncomeExpensesAndSavingsRate()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var mtd = body.GetProperty("mtd");

        mtd.TryGetProperty("currencyCode", out _).Should().BeTrue();
        mtd.TryGetProperty("currencySymbol", out _).Should().BeTrue();
        mtd.TryGetProperty("income", out _).Should().BeTrue();
        mtd.TryGetProperty("expenses", out _).Should().BeTrue();
        mtd.TryGetProperty("savingsRate", out var rate).Should().BeTrue();
        rate.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public async Task GetSummary_NetWorthEntryHasExpectedFields()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var netWorth = body.GetProperty("netWorth");

        foreach (var entry in netWorth.EnumerateArray())
        {
            entry.TryGetProperty("currencyCode", out _).Should().BeTrue();
            entry.TryGetProperty("currencySymbol", out _).Should().BeTrue();
            entry.TryGetProperty("assets", out _).Should().BeTrue();
            entry.TryGetProperty("liabilities", out _).Should().BeTrue();
            entry.TryGetProperty("netWorth", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetDashboardRoot_ServesSpaShell()
    {
        // Stage 11: the Razor /Dashboard → /app/ 302 redirect stub was deleted with all
        // Razor controllers. The legacy path now falls through to the SPA fallback
        // (MapFallbackToFile) and serves the app shell at 200; React Router renders the
        // route client-side. No server redirect remains.
        using var noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await noRedirectClient.GetAsync("/Dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }
}
