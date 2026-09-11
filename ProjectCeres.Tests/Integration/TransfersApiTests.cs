using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationParallel2")]
public class TransfersApiTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTransferIds = [];
    private readonly List<Guid> _seededAccountIds = [];

    public TransfersApiTests(TestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb)
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
        {
            var rows = await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ToListAsync();
            db.Transfers.RemoveRange(rows);
        }
        if (_seededAccountIds.Count > 0)
        {
            var rows = await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ToListAsync();
            db.Accounts.RemoveRange(rows);
        }
        await db.SaveChangesAsync();
    }

    private Account SeedAccount(AppDbContext db, string name, int accountTypeId = 1, int currencyId = 1)
    {
        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = name,
            AccountTypeId = accountTypeId,
            CurrencyId    = currencyId,
            IsActive      = true
        };
        db.Accounts.Add(account);
        _seededAccountIds.Add(account.Id);
        return account;
    }

    [Fact]
    public async Task Post_Returns201_AndCreatesTransfer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = SeedAccount(db, "Transfer-Source");
        var dest   = SeedAccount(db, "Transfer-Dest");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transfers", new
        {
            date            = "2026-04-15",
            amount          = 100.00m,
            sourceAccountId = source.Id,
            destAccountId   = dest.Id,
            description     = "Test transfer"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("id", out var idProp).Should().BeTrue();
        var newId = idProp.GetGuid();
        _seededTransferIds.Add(newId);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await verifyDb.Transfers.FindAsync(newId);
        saved.Should().NotBeNull();
        saved!.Amount.Should().Be(100.00m);
        saved.Description.Should().Be("Test transfer");
    }

    [Fact]
    public async Task Post_Returns422_WhenSourceEqualsDest()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = SeedAccount(db, "Transfer-SameSrc");
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/transfers", new
        {
            date            = "2026-04-15",
            amount          = 50.00m,
            sourceAccountId = account.Id,
            destAccountId   = account.Id,
            description     = "Self transfer"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
