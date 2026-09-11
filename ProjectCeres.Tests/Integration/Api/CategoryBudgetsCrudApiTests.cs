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
public class CategoryBudgetsCrudApiTests : IntegrationTestBase<Bucket4Factory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly Bucket4Factory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededBudgetIds = [];

    public CategoryBudgetsCrudApiTests(Bucket4Factory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
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
            await db.CategoryBudgets.Where(cb => _seededBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetList_returns_active_budgets_with_currency_filter()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 250m
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var response = await _client.GetAsync("/api/category-budgets?currency=EUR");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("currentPeriodSpend"); // shape sanity
    }

    [Fact]
    public async Task Post_then_Get_works_end_to_end()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 750m
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var read = await _client.GetAsync($"/api/category-budgets/{id}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await read.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("limitAmount").GetDecimal().Should().Be(750m);
        dto.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Post_returns_409_when_active_dup_exists()
    {
        var first = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 500m
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(firstId);

        var second = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 800m
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("DUPLICATE_BUDGET");
        body.GetProperty("error").GetProperty("existingBudgetId").GetGuid().Should().Be(firstId);
        body.GetProperty("error").GetProperty("existingIsActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Post_returns_409_with_existingIsActive_false_when_archived_dup_exists()
    {
        var first = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 500m
        });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(firstId);

        await _client.PatchAsync($"/api/category-budgets/{firstId}/archive", null);

        var second = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 700m
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("existingIsActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Put_updates_limit_amount()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var update = await _client.PutAsJsonAsync($"/api/category-budgets/{id}", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 900m,
            isActive    = true
        });
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.LimitAmount.Should().Be(900m);
    }

    [Fact]
    public async Task Archive_sets_isActive_false()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        var archive = await _client.PatchAsync($"/api/category-budgets/{id}/archive", null);
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Reactivate_after_archive_sets_isActive_true()
    {
        var create = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 600m
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededBudgetIds.Add(id);

        await _client.PatchAsync($"/api/category-budgets/{id}/archive", null);
        var react = await _client.PatchAsync($"/api/category-budgets/{id}/reactivate", null);
        react.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.CategoryBudgets.FindAsync(id);
        fresh!.IsActive.Should().BeTrue();
    }
}
