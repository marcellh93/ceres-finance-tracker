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
