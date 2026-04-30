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
public class TransfersCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransfersCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransferIds.Count > 0)
            await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid SourceId, Guid DestId, Guid TransferId)> SeedTransferAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var src = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"TrCrud-Src-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        var dest = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"TrCrud-Dst-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.AddRange(src, dest);

        var tr = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = new DateOnly(2026, 4, 10),
            Amount          = 200m,
            SourceAccountId = src.Id,
            DestAccountId   = dest.Id,
            Description     = "Initial transfer",
            IsCleared       = false
        };
        db.Transfers.Add(tr);

        await db.SaveChangesAsync();
        _seededAccountIds.Add(src.Id);
        _seededAccountIds.Add(dest.Id);
        _seededTransferIds.Add(tr.Id);
        return (src.Id, dest.Id, tr.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (srcId, destId, trId) = await SeedTransferAsync();

        var response = await _client.GetAsync($"/api/transfers/{trId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(trId);
        body.GetProperty("sourceAccountId").GetGuid().Should().Be(srcId);
        body.GetProperty("destAccountId").GetGuid().Should().Be(destId);
        body.GetProperty("amount").GetDecimal().Should().Be(200m);
        body.GetProperty("description").GetString().Should().Be("Initial transfer");
        body.GetProperty("isCleared").GetBoolean().Should().BeFalse();
        body.GetProperty("attachments").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/transfers/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Returns204_AndUpdatesFields()
    {
        var (srcId, destId, trId) = await SeedTransferAsync();

        // Seed a second source account so the SourceAccountId round-trip is non-degenerate.
        Guid newSourceId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newSource = new Account
            {
                Id = Guid.NewGuid(),
                Name = $"TrCrud-Src2-{Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId = 1,
                IsActive = true
            };
            seedDb.Accounts.Add(newSource);
            await seedDb.SaveChangesAsync();
            _seededAccountIds.Add(newSource.Id);
            newSourceId = newSource.Id;
        }

        var response = await _client.PutAsJsonAsync($"/api/transfers/{trId}", new
        {
            date            = "2026-04-20",
            amount          = 350m,
            sourceAccountId = newSourceId,
            destAccountId   = destId,
            description     = "Updated transfer",
            isCleared       = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Transfers.FindAsync(trId);
        saved!.Amount.Should().Be(350m);
        saved.Date.Should().Be(new DateOnly(2026, 4, 20));
        saved.SourceAccountId.Should().Be(newSourceId);
        saved.Description.Should().Be("Updated transfer");
        saved.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Put_Returns422_WhenSourceEqualsDest()
    {
        var (srcId, _, trId) = await SeedTransferAsync();

        var response = await _client.PutAsJsonAsync($"/api/transfers/{trId}", new
        {
            date            = "2026-04-15",
            amount          = 100m,
            sourceAccountId = srcId,
            destAccountId   = srcId,
            description     = (string?)null,
            isCleared       = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        var details = body.GetProperty("error").GetProperty("details").EnumerateArray().ToList();
        details.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_Returns204_AndHardDeletes()
    {
        var (_, _, trId) = await SeedTransferAsync();

        var response = await _client.DeleteAsync($"/api/transfers/{trId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Transfers.FindAsync(trId)).Should().BeNull();
        _seededTransferIds.Remove(trId);
    }

    [Fact]
    public async Task Put_Returns422_WhenCrossCurrency()
    {
        var (srcId, destId, trId) = await SeedTransferAsync();

        // Seed a destination account in a different currency.
        Guid otherCurrencyDestId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherDest = new Account
            {
                Id = Guid.NewGuid(),
                Name = $"Tr-OtherCcy-{Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId = 2,
                IsActive = true
            };
            seedDb.Accounts.Add(otherDest);
            await seedDb.SaveChangesAsync();
            _seededAccountIds.Add(otherDest.Id);
            otherCurrencyDestId = otherDest.Id;
        }

        var response = await _client.PutAsJsonAsync($"/api/transfers/{trId}", new
        {
            date = "2026-04-15",
            amount = 100m,
            sourceAccountId = srcId,
            destAccountId = otherCurrencyDestId,
            description = "cross-ccy",
            isCleared = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        body.GetProperty("error").GetProperty("details").GetArrayLength().Should().Be(0);
    }
}
