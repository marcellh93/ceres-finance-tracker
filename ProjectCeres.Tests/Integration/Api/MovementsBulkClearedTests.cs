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
public class MovementsBulkClearedTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public MovementsBulkClearedTests(TestWebApplicationFactory factory)
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
        if (_seededAccountIds.Count > 0)     await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Post_MarksMatchingTransactionsCleared()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"BC-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx1 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 5), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        var tx2 = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 8), Amount = 1m, AccountId = account.Id, CategoryId = HousingCategoryId };
        db.Accounts.Add(account); db.Transactions.AddRange(tx1, tx2);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.AddRange([tx1.Id, tx2.Id]);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            accountId = account.Id,
            type = "transaction"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cleared").GetInt32().Should().Be(2);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.Transactions.FindAsync(tx1.Id))!.IsCleared.Should().BeTrue();
        (await verifyDb.Transactions.FindAsync(tx2.Id))!.IsCleared.Should().BeTrue();
    }

    [Fact]
    public async Task Post_Returns400_WhenInvalidType()
    {
        var response = await _client.PostAsJsonAsync("/api/movements/bulk-cleared", new
        {
            from = "2026-04-01",
            to   = "2026-04-30",
            type = "bogus"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
