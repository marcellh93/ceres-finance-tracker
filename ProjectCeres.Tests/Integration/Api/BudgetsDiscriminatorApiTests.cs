using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel2")]
public class BudgetsDiscriminatorApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededCategoryBudgetIds = [];
    private readonly List<Guid> _seededBudgetIds = [];

    public BudgetsDiscriminatorApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededCategoryBudgetIds.Count > 0)
            await db.CategoryBudgets.Where(cb => _seededCategoryBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
        if (_seededBudgetIds.Count > 0)
            await db.Budgets.Where(b => _seededBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetKind_returns_CategoryBudget_when_id_is_a_category_budget()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cb = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 100m,
            IsActive    = true
        };
        db.CategoryBudgets.Add(cb);
        await db.SaveChangesAsync();
        _seededCategoryBudgetIds.Add(cb.Id);

        var response = await _client.GetAsync($"/api/budgets/{cb.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("id").GetGuid().Should().Be(cb.Id);
        dto.GetProperty("kind").GetString().Should().Be("CategoryBudget");
    }

    [Fact]
    public async Task GetKind_returns_GoalBudget_when_id_is_a_budget()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var b = new Budget
        {
            Id           = Guid.NewGuid(),
            Name         = $"BD-{Guid.NewGuid():N}",
            TargetAmount = 1000m,
            CurrencyId   = 1,
            StartDate    = new DateOnly(2026, 1, 1),
            GoalType     = "Spending",
            IsActive     = true
        };
        db.Budgets.Add(b);
        await db.SaveChangesAsync();
        _seededBudgetIds.Add(b.Id);

        var response = await _client.GetAsync($"/api/budgets/{b.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("id").GetGuid().Should().Be(b.Id);
        dto.GetProperty("kind").GetString().Should().Be("GoalBudget");
    }

    [Fact]
    public async Task GetKind_returns_404_when_id_matches_neither()
    {
        var response = await _client.GetAsync($"/api/budgets/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
