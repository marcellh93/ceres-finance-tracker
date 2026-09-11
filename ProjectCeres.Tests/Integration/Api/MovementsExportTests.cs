using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel3")]
public class MovementsExportTests : IntegrationTestBase<TestWebApplicationFactory>, IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public MovementsExportTests(TestWebApplicationFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb)
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
    public async Task Export_Returns200_WithCsvBody()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Ex-A-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var tx = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 11), Amount = 7m, AccountId = account.Id, CategoryId = HousingCategoryId, Description = "row,with,commas" };
        db.Accounts.Add(account); db.Transactions.Add(tx);
        _seededAccountIds.Add(account.Id); _seededTransactionIds.Add(tx.Id);
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/movements/export.csv?from=2026-04-01&to=2026-04-30&type=transaction");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // UTF-8 BOM (EF BB BF) so Excel/Numbers on macOS render multibyte glyphs correctly.
        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });

        var body = System.Text.Encoding.UTF8.GetString(bytes);
        body.Should().Contain("Date,Type,Amount");
        body.Should().Contain("\"row,with,commas\"");
        body.Should().NotContain(",true");
        body.Should().NotContain(",false");
    }

    [Fact]
    public async Task Export_FilenameIncludesTypeAndDateRange()
    {
        var response = await _client.GetAsync("/api/movements/export.csv?from=2026-04-01&to=2026-04-30&type=transaction");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull();
        var fileName = disposition!.FileNameStar ?? disposition.FileName?.Trim('"');
        fileName.Should().Be("movements_transactions_2026-04-01_2026-04-30.csv");
    }

    [Fact]
    public async Task Export_FilenameWithoutFiltersUsesTodayDate()
    {
        var response = await _client.GetAsync("/api/movements/export.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull();
        var fileName = disposition!.FileNameStar ?? disposition.FileName?.Trim('"');
        fileName.Should().StartWith("movements_").And.EndWith(".csv");
    }
}
