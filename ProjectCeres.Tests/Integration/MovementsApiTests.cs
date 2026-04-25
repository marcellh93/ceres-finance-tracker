using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WebApplicationFactory tests for Stage 2.5.3:
/// — PATCH /api/movements/{id}/cleared routes to the correct service based on type
/// — Unknown id → 404; unknown type → 400
///
/// The factory overrides the connection string to project_ceres_test so these tests
/// never touch the dev database. Seeded rows are deleted in DisposeAsync.
/// </summary>
[Collection("IntegrationTests")]
public class MovementsApiTests : IAsyncLifetime
{
    private static readonly Guid SalaryCategoryId = new("20000000-0000-0000-0000-000000000002");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];

    public MovementsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_seededTransactionIds.Count > 0)
            await db.Transactions.Where(t => _seededTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();

        if (_seededTransferIds.Count > 0)
            await db.Transfers.Where(t => _seededTransferIds.Contains(t.Id)).ExecuteDeleteAsync();

        if (_seededAccountIds.Count > 0)
            await db.Accounts.Where(a => _seededAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedTransactionAsync(bool isCleared = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"MvApi-Acct-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);

        var tx = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = DateOnly.FromDateTime(DateTime.Today),
            Amount     = 50m,
            AccountId  = account.Id,
            CategoryId = SalaryCategoryId,
            IsCleared  = isCleared,
            CreatedAt  = DateTime.UtcNow
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        _seededTransactionIds.Add(tx.Id);

        return tx.Id;
    }

    private async Task<Guid> SeedTransferAsync(bool isCleared = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var src = new Account { Id = Guid.NewGuid(), Name = $"MvApi-Src-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var dst = new Account { Id = Guid.NewGuid(), Name = $"MvApi-Dst-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        db.Accounts.AddRange(src, dst);
        await db.SaveChangesAsync();
        _seededAccountIds.AddRange([src.Id, dst.Id]);

        var tr = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = DateOnly.FromDateTime(DateTime.Today),
            Amount          = 100m,
            SourceAccountId = src.Id,
            DestAccountId   = dst.Id,
            IsCleared       = isCleared,
            CreatedAt       = DateTime.UtcNow
        };
        db.Transfers.Add(tr);
        await db.SaveChangesAsync();
        _seededTransferIds.Add(tr.Id);

        return tr.Id;
    }

    private AppDbContext GetDb()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    // -------------------------------------------------------------------------
    // PATCH transaction cleared → 200; DB updated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchCleared_Transaction_Returns200_AndSetsIsClearedTrue()
    {
        var id = await SeedTransactionAsync(isCleared: false);

        var payload = new StringContent(
            JsonSerializer.Serialize(new { type = "transaction", cleared = true }),
            Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync($"/api/movements/{id}/cleared", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tx = await GetDb().Transactions.FindAsync(id);
        tx!.IsCleared.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // PATCH transaction cleared → can toggle back to false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchCleared_Transaction_CanToggleBackToFalse()
    {
        var id = await SeedTransactionAsync(isCleared: true);

        var payload = new StringContent(
            JsonSerializer.Serialize(new { type = "transaction", cleared = false }),
            Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync($"/api/movements/{id}/cleared", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tx = await GetDb().Transactions.FindAsync(id);
        tx!.IsCleared.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // PATCH transfer cleared → 200; DB updated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchCleared_Transfer_Returns200_AndSetsIsClearedTrue()
    {
        var id = await SeedTransferAsync(isCleared: false);

        var payload = new StringContent(
            JsonSerializer.Serialize(new { type = "transfer", cleared = true }),
            Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync($"/api/movements/{id}/cleared", payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tr = await GetDb().Transfers.FindAsync(id);
        tr!.IsCleared.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // PATCH with unknown id → 404
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchCleared_UnknownId_Returns404()
    {
        var payload = new StringContent(
            JsonSerializer.Serialize(new { type = "transaction", cleared = true }),
            Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync($"/api/movements/{Guid.NewGuid()}/cleared", payload);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // PATCH with unknown type → 400
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PatchCleared_UnknownType_Returns400()
    {
        var payload = new StringContent(
            JsonSerializer.Serialize(new { type = "unknown", cleared = true }),
            Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync($"/api/movements/{Guid.NewGuid()}/cleared", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
