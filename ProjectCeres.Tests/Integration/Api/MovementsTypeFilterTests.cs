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
public class MovementsTypeFilterTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededPaymentIds = [];

    public MovementsTypeFilterTests(TestWebApplicationFactory factory)
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
        if (_seededPaymentIds.Count > 0)     await db.LiabilityPayments.Where(p => _seededPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task SeedOneOfEachAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var a1 = new Account { Id = Guid.NewGuid(), Name = $"Tf-A1-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var a2 = new Account { Id = Guid.NewGuid(), Name = $"Tf-A2-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var liab = new Account { Id = Guid.NewGuid(), Name = $"Tf-L-{Guid.NewGuid():N}", AccountTypeId = 2, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(a1, a2, liab);
        _seededAccountIds.AddRange([a1.Id, a2.Id, liab.Id]);

        var tx = new Transaction { Id = Guid.NewGuid(), Date = date, Amount = 10m, AccountId = a1.Id, CategoryId = HousingCategoryId, Description = "tx-only" };
        var tr = new Transfer    { Id = Guid.NewGuid(), Date = date, Amount = 20m, SourceAccountId = a1.Id, DestAccountId = a2.Id, Description = "tr-only" };
        var lp = new LiabilityPayment { Id = Guid.NewGuid(), Date = date, Amount = 30m, AssetAccountId = a1.Id, LiabilityAccountId = liab.Id, Description = "lp-only" };
        db.Transactions.Add(tx);
        db.Transfers.Add(tr);
        db.LiabilityPayments.Add(lp);
        _seededTransactionIds.Add(tx.Id);
        _seededTransferIds.Add(tr.Id);
        _seededPaymentIds.Add(lp.Id);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMovements_FilterByTransaction_ReturnsOnlyTransactions()
    {
        var date = new DateOnly(2026, 4, 22);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=transaction");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "Transaction");
    }

    [Fact]
    public async Task GetMovements_FilterByTransfer_ReturnsOnlyTransfers()
    {
        var date = new DateOnly(2026, 4, 23);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=transfer");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "Transfer");
    }

    [Fact]
    public async Task GetMovements_FilterByLiabilityPayment_ReturnsOnlyPayments()
    {
        var date = new DateOnly(2026, 4, 24);
        await SeedOneOfEachAsync(date);

        var response = await _client.GetAsync($"/api/movements?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&type=liabilitypayment");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").EnumerateArray().Should().OnlyContain(m => m.GetProperty("movementType").GetString() == "LiabilityPayment");
    }

    [Fact]
    public async Task GetMovements_InvalidType_Returns400()
    {
        var response = await _client.GetAsync($"/api/movements?type=bogus");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
