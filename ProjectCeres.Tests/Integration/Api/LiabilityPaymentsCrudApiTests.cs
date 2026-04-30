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
public class LiabilityPaymentsCrudApiTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededPaymentIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public LiabilityPaymentsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededPaymentIds.Count > 0)
            await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid AssetId, Guid LiabilityId, Guid PaymentId)> SeedPaymentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var asset = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"LpCrud-Asset-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        var liab = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"LpCrud-Liab-{Guid.NewGuid():N}",
            AccountTypeId = 2,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.AddRange(asset, liab);

        var lp = new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            Date               = new DateOnly(2026, 4, 12),
            Amount             = 150m,
            AssetAccountId     = asset.Id,
            LiabilityAccountId = liab.Id,
            Description        = "Initial payment",
            IsCleared          = false
        };
        db.LiabilityPayments.Add(lp);

        await db.SaveChangesAsync();
        _seededAccountIds.Add(asset.Id);
        _seededAccountIds.Add(liab.Id);
        _seededPaymentIds.Add(lp.Id);
        return (asset.Id, liab.Id, lp.Id);
    }

    [Fact]
    public async Task Get_Returns200_WithEditDto()
    {
        var (assetId, liabId, paymentId) = await SeedPaymentAsync();

        var response = await _client.GetAsync($"/api/liability-payments/{paymentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(paymentId);
        body.GetProperty("assetAccountId").GetGuid().Should().Be(assetId);
        body.GetProperty("liabilityAccountId").GetGuid().Should().Be(liabId);
        body.GetProperty("amount").GetDecimal().Should().Be(150m);
        body.GetProperty("date").GetString().Should().Be("2026-04-12");
        body.GetProperty("description").GetString().Should().Be("Initial payment");
        body.GetProperty("isCleared").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        var response = await _client.GetAsync($"/api/liability-payments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Returns204_AndUpdates()
    {
        var (assetId, liabId, paymentId) = await SeedPaymentAsync();

        var response = await _client.PutAsJsonAsync($"/api/liability-payments/{paymentId}", new
        {
            date               = "2026-04-18",
            amount             = 222m,
            assetAccountId     = assetId,
            liabilityAccountId = liabId,
            description        = "Updated",
            isCleared          = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.LiabilityPayments.FindAsync(paymentId);
        saved!.Amount.Should().Be(222m);
        saved.Date.Should().Be(new DateOnly(2026, 4, 18));
        saved.Description.Should().Be("Updated");
        saved.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_Returns204_AndHardDeletes()
    {
        var (_, _, paymentId) = await SeedPaymentAsync();

        var response = await _client.DeleteAsync($"/api/liability-payments/{paymentId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.LiabilityPayments.FindAsync(paymentId)).Should().BeNull();
        _seededPaymentIds.Remove(paymentId);
    }

    [Fact]
    public async Task Put_Returns422_WhenCrossCurrency()
    {
        var (assetId, liabilityId, paymentId) = await SeedPaymentAsync();

        // Seed a liability account in a different currency.
        Guid otherCurrencyLiabilityId;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherLiability = new Account
            {
                Id = Guid.NewGuid(),
                Name = $"Lp-OtherCcy-{Guid.NewGuid():N}",
                AccountTypeId = 2,
                CurrencyId = 2,
                IsActive = true
            };
            seedDb.Accounts.Add(otherLiability);
            await seedDb.SaveChangesAsync();
            _seededAccountIds.Add(otherLiability.Id);
            otherCurrencyLiabilityId = otherLiability.Id;
        }

        var response = await _client.PutAsJsonAsync($"/api/liability-payments/{paymentId}", new
        {
            date = "2026-04-18",
            amount = 100m,
            assetAccountId = assetId,
            liabilityAccountId = otherCurrencyLiabilityId,
            description = "cross-ccy",
            isCleared = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        body.GetProperty("error").GetProperty("details").GetArrayLength().Should().Be(0);
    }
}
