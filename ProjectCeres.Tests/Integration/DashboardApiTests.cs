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
    public async Task GetNetWorthTrend_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/net-worth-trend");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        // Should return up to 6 monthly entries
        body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(6);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("month", out _).Should().BeTrue();
            item.TryGetProperty("assets", out _).Should().BeTrue();
            item.TryGetProperty("liabilities", out _).Should().BeTrue();
            item.TryGetProperty("netWorth", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetIncomeExpense_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/dashboard/income-expense");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(6);

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
        body.GetArrayLength().Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(6);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("month", out _).Should().BeTrue();
            item.TryGetProperty("netFlow", out _).Should().BeTrue();
        }
    }
}
