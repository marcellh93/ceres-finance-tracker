using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WAF tests for GET /Transactions/Export.
///
/// Verifies:
///   — 200 OK + text/csv content-type (smoke checks)
///   — Response body is parseable CSV with the correct header and row count
///   — Date-range filter (from/to) returns only matching rows
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing / Rent (Expense)
/// </summary>
[Collection("IntegrationTests")]
public class TransactionExportControllerTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];

    public TransactionExportControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_seededTransactionIds.Count > 0)
            await db.Transactions
                .Where(t => _seededTransactionIds.Contains(t.Id))
                .ExecuteDeleteAsync();

        if (_seededAccountIds.Count > 0)
            await db.Accounts
                .Where(a => _seededAccountIds.Contains(a.Id))
                .ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedAssetAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"WAF Export {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);
        return account.Id;
    }

    private async Task<Guid> SeedTransactionAsync(Guid accountId, DateOnly date, decimal amount = 100m, string description = "Test")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var txn = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = date,
            Amount      = amount,
            AccountId   = accountId,
            CategoryId  = HousingCategoryId,
            Description = description,
            IsCleared   = false,
            CreatedAt   = DateTime.UtcNow
        };
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        _seededTransactionIds.Add(txn.Id);
        return txn.Id;
    }

    // -------------------------------------------------------------------------
    // Smoke checks — content-type
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetExport_NoFilter_Returns200WithCsvContentType()
    {
        var response = await _client.GetAsync("/Transactions/Export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task GetExport_WithDateRange_Returns200WithCsvContentType()
    {
        var response = await _client.GetAsync("/Transactions/Export?from=2025-01-01&to=2025-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    // -------------------------------------------------------------------------
    // Body content — header + row count
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetExport_NoFilter_CsvBodyHasCorrectHeaderAndRowCount()
    {
        var accountId = await SeedAssetAccountAsync();
        await SeedTransactionAsync(accountId, new DateOnly(2025, 6, 1), 100m, "Rent June");
        await SeedTransactionAsync(accountId, new DateOnly(2025, 6, 2), 200m, "Rent extra");
        await SeedTransactionAsync(accountId, new DateOnly(2025, 6, 3), 300m, "Utilities");

        var response = await _client.GetAsync("/Transactions/Export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("Date,Account,Category,Type,Description,Amount",
            because: "first line must be the CSV header");

        // At least 1 header + 3 data rows; the DB may contain other rows from prior tests,
        // so we assert >= 4 rather than exactly 4.
        lines.Length.Should().BeGreaterThanOrEqualTo(4,
            because: "there must be at least 3 seeded data rows plus the header");
    }

    [Fact]
    public async Task GetExport_WithDateRangeFilter_ReturnsOnlyMatchingRows()
    {
        var accountId = await SeedAssetAccountAsync();

        // 2 rows in January 2025
        await SeedTransactionAsync(accountId, new DateOnly(2025, 1, 10), 50m,  "Jan row 1");
        await SeedTransactionAsync(accountId, new DateOnly(2025, 1, 20), 75m,  "Jan row 2");

        // 1 row in February 2025 — must be excluded by the filter
        await SeedTransactionAsync(accountId, new DateOnly(2025, 2, 5),  90m,  "Feb row");

        var response = await _client.GetAsync(
            $"/Transactions/Export?from=2025-01-01&to=2025-01-31");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body  = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be("Date,Account,Category,Type,Description,Amount",
            because: "first line must be the CSV header");

        // Exactly 1 header + 2 January rows (the February row must not appear)
        lines.Length.Should().Be(3,
            because: "the date-range filter should return exactly 2 data rows for January 2025");
    }
}
