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
public class CategoriesCrudApiTests : IntegrationTestBase<Bucket4Factory>, IAsyncLifetime
{
    private static readonly Guid OpeningBalanceCategoryId = new("20000000-0000-0000-0000-000000000001"); // IsSystem
    private static readonly Guid UncategorizedExpenseId   = new("20000000-0000-0000-0000-000000000026"); // reserved
    private static readonly Guid SalaryCategoryId         = new("20000000-0000-0000-0000-000000000002"); // user-editable seed

    private readonly Bucket4Factory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _createdIds = [];

    public CategoriesCrudApiTests(Bucket4Factory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_createdIds.Count > 0)
            await db.Categories.Where(c => _createdIds.Contains(c.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Get_returns_active_categories_by_default()
    {
        var res = await _client.GetAsync("/api/categories");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Should().NotBeEmpty();
        rows!.Should().OnlyContain(r => r.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Get_filters_by_typeId()
    {
        var res = await _client.GetAsync("/api/categories?typeId=1"); // Income
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Should().OnlyContain(r => r.GetProperty("categoryTypeId").GetInt32() == 1);
    }

    [Fact]
    public async Task Post_creates_category()
    {
        var res = await _client.PostAsJsonAsync("/api/categories", new
        {
            name           = "Coffee runs",
            categoryTypeId = 2,
            lifestyleTag   = "Wants"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        var id  = dto.GetProperty("id").GetGuid();
        _createdIds.Add(id);
        dto.GetProperty("name").GetString().Should().Be("Coffee runs");
        dto.GetProperty("isActive").GetBoolean().Should().BeTrue();
        dto.GetProperty("isSystem").GetBoolean().Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.Categories.FindAsync(id);
        fresh!.UserId.Should().Be(new Guid("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task Post_returns_422_on_missing_fields()
    {
        var res = await _client.PostAsJsonAsync("/api/categories", new { name = "" });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Post_returns_422_on_invalid_category_type()
    {
        var res = await _client.PostAsJsonAsync("/api/categories", new
        {
            name           = "Bad type",
            categoryTypeId = 999
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_CATEGORY_TYPE");
    }

    [Fact]
    public async Task Patch_updates_name_and_lifestyle()
    {
        var create = await _client.PostAsJsonAsync("/api/categories", new { name = "Edit me", categoryTypeId = 2 });
        var createBody = await create.Content.ReadAsStringAsync();
        create.StatusCode.Should().Be(HttpStatusCode.Created, "create body: " + createBody);
        var id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetGuid();
        _createdIds.Add(id);

        var res = await _client.PatchAsJsonAsync($"/api/categories/{id}", new { name = "Edited", lifestyleTag = "Needs" });
        var raw = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, "patch body: " + raw);
        var dto = JsonDocument.Parse(raw).RootElement;
        dto.GetProperty("name").GetString().Should().Be("Edited");
        dto.GetProperty("lifestyleTag").GetString().Should().Be("Needs");
    }

    [Fact]
    public async Task Patch_rejects_system_category()
    {
        // Stage 7 Task 9 backfill: Opening Balance was stamped with the sentinel UserId
        // (StampOpeningBalanceWithSentinel migration) so the Ledger flow can resolve the
        // opening-balance Transaction's Category. The row is now visible to the sentinel
        // test user; CategoryPolicies.CanEdit catches IsSystem = true and returns 422 with
        // SYSTEM_CATEGORY_IMMUTABLE — the original pre-Stage-7 contract restored.
        var res = await _client.PatchAsJsonAsync($"/api/categories/{OpeningBalanceCategoryId}", new { name = "Hacked" });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("SYSTEM_CATEGORY_IMMUTABLE");
    }

    [Fact]
    public async Task Patch_rejects_reserved_uncategorized()
    {
        var res = await _client.PatchAsJsonAsync($"/api/categories/{UncategorizedExpenseId}", new { name = "Hacked" });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("SYSTEM_CATEGORY_IMMUTABLE");
    }

    [Fact]
    public async Task Patch_returns_404_when_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"/api/categories/{Guid.NewGuid()}", new { name = "x" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_deactivates_unused_category()
    {
        var create = await _client.PostAsJsonAsync("/api/categories", new { name = "Disposable", categoryTypeId = 2 });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdIds.Add(id);

        var res = await _client.PatchAsync($"/api/categories/{id}/archive", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fresh = await db.Categories.FindAsync(id);
        fresh!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Archive_rejects_category_in_use()
    {
        // Salary is seeded and likely unused in fresh test DB — attach a transaction so it qualifies as "in use".
        var accountId = new Guid("10000000-0000-0000-0000-000000000002"); // Checking
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                Date        = DateOnly.FromDateTime(DateTime.Today),
                Amount      = 100m,
                Description = "test-in-use",
                AccountId   = accountId,
                CategoryId  = SalaryCategoryId,
                CreatedAt   = DateTime.UtcNow,
                UserId      = new Guid("00000000-0000-0000-0000-000000000001")
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var res = await _client.PatchAsync($"/api/categories/{SalaryCategoryId}/archive", null);
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetProperty("code").GetString().Should().Be("CATEGORY_IN_USE");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Transactions.Where(t => t.Description == "test-in-use").ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Archive_rejects_system_category()
    {
        // Stage 7 Task 9 backfill: Opening Balance was stamped with the sentinel UserId
        // (StampOpeningBalanceWithSentinel migration) so the Ledger flow can resolve the
        // opening-balance Transaction's Category. The row is now visible to the sentinel
        // test user; CategoryPolicies.CanEdit catches IsSystem = true and returns 422 with
        // SYSTEM_CATEGORY_IMMUTABLE — the original pre-Stage-7 contract restored.
        var res = await _client.PatchAsync($"/api/categories/{OpeningBalanceCategoryId}/archive", null);
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("SYSTEM_CATEGORY_IMMUTABLE");
    }

    [Fact]
    public async Task Archive_returns_404_when_unknown_id()
    {
        var res = await _client.PatchAsync($"/api/categories/{Guid.NewGuid()}/archive", null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_returns_seeded_category()
    {
        var res = await _client.GetAsync($"/api/categories/{SalaryCategoryId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("name").GetString().Should().Be("Salary");
    }

    [Fact]
    public async Task GetById_returns_404_when_unknown_id()
    {
        var res = await _client.GetAsync($"/api/categories/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CategoryTypes_returns_seeded_types()
    {
        var res = await _client.GetAsync("/api/category-types");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Should().HaveCount(2);
        rows!.Select(r => r.GetProperty("name").GetString()).Should().BeEquivalentTo(["Income", "Expense"]);
    }
}
