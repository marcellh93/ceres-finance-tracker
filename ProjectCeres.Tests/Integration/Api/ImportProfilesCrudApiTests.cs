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
public class ImportProfilesCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _createdIds = [];

    public ImportProfilesCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdIds.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ImportProfiles.Where(p => _createdIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private object MakeBody(string? name = null) => new
    {
        name    = name ?? $"BankCsv-{Guid.NewGuid():N}",
        format  = "Csv",
        mappings = new
        {
            dateColumn        = "Date",
            amountColumn      = "Amount",
            descriptionColumn = "Description",
            flipDebitSign     = false
        }
    };

    private async Task<Guid> CreateOne()
    {
        var res = await _client.PostAsJsonAsync("/api/import-profiles", MakeBody());
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdIds.Add(id);
        return id;
    }

    [Fact]
    public async Task Post_creates_profile_with_owner()
    {
        var id = await CreateOne();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportProfiles.AsNoTracking().FirstAsync(p => p.Id == id);
        row.UserId.Should().Be(new Guid("00000000-0000-0000-0000-000000000001"));
        row.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Post_returns_422_on_invalid_format()
    {
        var res = await _client.PostAsJsonAsync("/api/import-profiles", new
        {
            name    = "X",
            format  = "Pdf",
            mappings = new { dateColumn = "D", amountColumn = "A", descriptionColumn = "Desc" }
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_FORMAT");
    }

    [Fact]
    public async Task Get_returns_active_profiles_only_by_default()
    {
        var id = await CreateOne();
        var res = await _client.GetAsync("/api/import-profiles");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);
        rows!.Should().OnlyContain(r => r.GetProperty("deletedAt").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Patch_updates_name_and_mappings()
    {
        var id = await CreateOne();
        var res = await _client.PatchAsJsonAsync($"/api/import-profiles/{id}", new
        {
            name = "Renamed",
            mappings = new
            {
                dateColumn        = "Fecha",
                amountColumn      = "Importe",
                descriptionColumn = "Concepto",
                flipDebitSign     = true
            }
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("name").GetString().Should().Be("Renamed");
        dto.GetProperty("mappings").GetProperty("flipDebitSign").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Patch_returns_404_when_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"/api/import-profiles/{Guid.NewGuid()}", new
        {
            name = "x",
            mappings = new { dateColumn = "D", amountColumn = "A", descriptionColumn = "Desc" }
        });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_soft_deletes_then_Recover_restores()
    {
        var id = await CreateOne();
        (await _client.DeleteAsync($"/api/import-profiles/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Default GET excludes deleted profiles.
        var listAfterDelete = await _client.GetFromJsonAsync<List<JsonElement>>("/api/import-profiles");
        listAfterDelete!.Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(id);

        // includeDeleted=true returns the soft-deleted row.
        var listIncludingDeleted = await _client.GetFromJsonAsync<List<JsonElement>>("/api/import-profiles?includeDeleted=true");
        listIncludingDeleted!.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);

        (await _client.PostAsync($"/api/import-profiles/{id}/recover", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ImportProfiles.FindAsync(id);
        row!.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Delete_returns_404_when_unknown_id()
    {
        var res = await _client.DeleteAsync($"/api/import-profiles/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_returns_404_for_intruder_row()
    {
        var intruderId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ImportProfiles.Add(new ImportProfile
            {
                Id             = intruderId,
                UserId         = Guid.NewGuid(),
                Name           = "intruder-profile",
                ColumnMappings = "{}",
                Format         = ImportFormat.Csv,
                CreatedAt      = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var res = await _client.GetAsync($"/api/import-profiles/{intruderId}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ImportProfiles.Where(p => p.Id == intruderId).ExecuteDeleteAsync();
        }
    }
}
