using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel4")]
public class GoalBudgetsProgressApiTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededBudgetIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public GoalBudgetsProgressApiTests(TestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededBudgetIds.Count > 0)
            await db.Budgets.Where(b => _seededBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Spending_goal_progress_sums_tagged_transactions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Sp-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        await db.SaveChangesAsync();

        var goal = new Budget { Id = Guid.NewGuid(), Name = "T", TargetAmount = 1000m, CurrencyId = 1, StartDate = new DateOnly(2026, 1, 1), GoalType = "Spending", IsActive = true };
        db.Budgets.Add(goal);
        _seededBudgetIds.Add(goal.Id);

        var t1 = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId, Date = new DateOnly(2026, 2, 1), Amount = 200m, BudgetId = goal.Id };
        var t2 = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId, Date = new DateOnly(2026, 3, 1), Amount = 150m, BudgetId = goal.Id };
        db.Transactions.AddRange(t1, t2);
        _seededTransactionIds.AddRange([t1.Id, t2.Id]);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/goal-budgets/{goal.Id}/progress");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("progress").GetDecimal().Should().Be(350m);
        dto.GetProperty("target").GetDecimal().Should().Be(1000m);
        dto.GetProperty("percentage").GetInt32().Should().Be(35);
    }
}
