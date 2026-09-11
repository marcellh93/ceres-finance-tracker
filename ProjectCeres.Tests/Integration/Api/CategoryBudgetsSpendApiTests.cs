using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel1")]
public class CategoryBudgetsSpendApiTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededBudgetIds = [];
    private int? _originalStartDay;

    public CategoryBudgetsSpendApiTests(TestWebApplicationFactory factory, Bucket1Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // Stage 7: scope every Settings query to the sentinel UserId. The test's
    // HTTP requests run under TestAuthenticationHandler which authenticates as
    // the sentinel user; the test mutates THAT user's PeriodStartDay for the
    // test's duration and restores it on cleanup. Post-Stage-7 the Settings
    // table is per-user, so an order-non-deterministic FirstAsync() could
    // target ANY user's row — wrong row mutated, wrong row restored, neither
    // applied to the SUT under test.
    private static readonly Guid SentinelUserId = new("00000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.Settings.SingleAsync(s => s.UserId == SentinelUserId);
        _originalStartDay = settings.PeriodStartDay;
        settings.PeriodStartDay = 25;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededBudgetIds.Count > 0)
            await db.CategoryBudgets.Where(cb => _seededBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_originalStartDay.HasValue)
        {
            var settings = await db.Settings.SingleAsync(s => s.UserId == SentinelUserId);
            settings.PeriodStartDay = _originalStartDay.Value;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetSpend_with_startDay25_includes_only_in_period_transactions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account { Id = Guid.NewGuid(), Name = $"BS-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);

        var budget = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 500m,
            IsActive    = true
        };
        db.CategoryBudgets.Add(budget);
        _seededBudgetIds.Add(budget.Id);

        var inPeriod = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId,
                                         Date = new DateOnly(2026, 5, 1), Amount = 100m };
        var outOfPeriod = new Transaction { Id = Guid.NewGuid(), AccountId = account.Id, CategoryId = HousingCategoryId,
                                            Date = new DateOnly(2026, 4, 24), Amount = 999m };
        db.Transactions.AddRange(inPeriod, outOfPeriod);
        _seededTransactionIds.AddRange([inPeriod.Id, outOfPeriod.Id]);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/category-budgets/{budget.Id}/spend?year=2026&month=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("spent").GetDecimal().Should().Be(100m);
        dto.GetProperty("periodStart").GetString().Should().Be("2026-04-25");
        dto.GetProperty("periodEnd").GetString().Should().Be("2026-05-24");
    }
}
