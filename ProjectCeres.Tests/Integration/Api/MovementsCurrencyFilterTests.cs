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
public class MovementsCurrencyFilterTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];

    public MovementsCurrencyFilterTests(TestWebApplicationFactory factory, Bucket1Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_seededTransactionIds.Count > 0) await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededTransferIds.Count > 0)    await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid eurAccount, Guid usdAccount, Guid eurTx, Guid usdTx)> SeedMixedCurrenciesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eur = new Account { Id = Guid.NewGuid(), Name = $"CFx-EUR-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var usd = new Account { Id = Guid.NewGuid(), Name = $"CFx-USD-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 2, IsActive = true };
        var eurTx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 11), Amount = 11m, AccountId = eur.Id, CategoryId = HousingCategoryId, Description = "EUR-marker" };
        var usdTx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 11), Amount = 22m, AccountId = usd.Id, CategoryId = HousingCategoryId, Description = "USD-marker" };
        db.Accounts.AddRange(eur, usd);
        db.Transactions.AddRange(eurTx, usdTx);
        _seededAccountIds.AddRange([eur.Id, usd.Id]);
        _seededTransactionIds.AddRange([eurTx.Id, usdTx.Id]);
        await db.SaveChangesAsync();
        return (eur.Id, usd.Id, eurTx.Id, usdTx.Id);
    }

    [Fact]
    public async Task GetMovements_FilteredByCurrency_OnlyReturnsThatCurrencyRows()
    {
        var (_, _, eurTx, usdTx) = await SeedMixedCurrenciesAsync();

        var response = await _client.GetAsync("/api/movements?from=2026-04-01&to=2026-04-30&currency=EUR");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(eurTx);
        ids.Should().NotContain(usdTx);
    }

    [Fact]
    public async Task ExportCsv_FilteredByCurrency_OnlyIncludesThatCurrencyRows()
    {
        await SeedMixedCurrenciesAsync();

        var response = await _client.GetAsync("/api/movements/export.csv?from=2026-04-01&to=2026-04-30&currency=USD");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("USD-marker");
        body.Should().NotContain("EUR-marker");
    }

    [Fact]
    public async Task ExportCsv_FilenameIncludesCurrencyCode()
    {
        await SeedMixedCurrenciesAsync();

        var response = await _client.GetAsync("/api/movements/export.csv?from=2026-04-01&to=2026-04-30&currency=EUR");
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition!.FileNameStar ?? disposition.FileName?.Trim('"');
        fileName.Should().Be("movements_eur_2026-04-01_2026-04-30.csv");
    }

    [Fact]
    public async Task BulkCleared_FilteredByCurrency_OnlyClearsThatCurrencyRows()
    {
        var (_, _, eurTx, usdTx) = await SeedMixedCurrenciesAsync();

        var body = new { from = "2026-04-01", to = "2026-04-30", currency = "EUR" };
        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var eur = await db.Transactions.FindAsync(eurTx);
        var usd = await db.Transactions.FindAsync(usdTx);
        eur!.IsCleared.Should().BeTrue();
        usd!.IsCleared.Should().BeFalse();
    }
}
