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

[Collection("IntegrationTests")]
public class ReconciliationReviewApiTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Guid _intruderUserId = Guid.NewGuid();
    private readonly List<Guid> _stagedIds = [];
    private readonly List<Guid> _accountIds = [];
    private readonly List<Guid> _transactionIds = [];

    public ReconciliationReviewApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_stagedIds.Count > 0)      await db.ImportStagedTransactions.Where(s => _stagedIds.Contains(s.Id)).ExecuteDeleteAsync();
        if (_transactionIds.Count > 0) await db.Transactions.Where(t => _transactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_accountIds.Count > 0)     await db.Accounts.Where(a => _accountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid AccountId, Guid TransactionId, Guid StagedId)> SeedSentinelStagedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Recon-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var matched = new Transaction { Id = Guid.NewGuid(), Date = new DateOnly(2026, 4, 15), Amount = 30m, AccountId = account.Id, CategoryId = HousingCategoryId, Description = "matched-tx", CreatedAt = DateTime.UtcNow };
        var staged = new ImportStagedTransaction
        {
            Id                  = Guid.NewGuid(),
            ImportedAt          = DateTime.UtcNow,
            AccountId           = account.Id,
            RawDate             = new DateOnly(2026, 4, 15),
            RawAmount           = 30m,
            RawDescription      = "imported-row",
            MatchedTransactionId = matched.Id,
            Status              = StagedTransactionStatus.Pending,
        };
        db.Accounts.Add(account);
        db.Transactions.Add(matched);
        db.ImportStagedTransactions.Add(staged);
        await db.SaveChangesAsync();
        _accountIds.Add(account.Id);
        _transactionIds.Add(matched.Id);
        _stagedIds.Add(staged.Id);
        return (account.Id, matched.Id, staged.Id);
    }

    private async Task<Guid> SeedIntruderStagedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"Recon-Intruder-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId };
        var staged = new ImportStagedTransaction
        {
            Id             = Guid.NewGuid(),
            UserId         = _intruderUserId,
            ImportedAt     = DateTime.UtcNow,
            AccountId      = account.Id,
            RawDate        = new DateOnly(2026, 4, 15),
            RawAmount      = 99m,
            RawDescription = "intruder-staged",
            Status         = StagedTransactionStatus.Pending,
        };
        db.Accounts.Add(account);
        db.ImportStagedTransactions.Add(staged);
        await db.SaveChangesAsync();
        _accountIds.Add(account.Id); _stagedIds.Add(staged.Id);
        return staged.Id;
    }

    [Fact]
    public async Task GetPending_returns_staged_rows()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.GetAsync("/api/reconciliation-review/pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(stagedId);
    }

    [Fact]
    public async Task GetPending_excludes_intruder_rows()
    {
        var intruderId = await SeedIntruderStagedAsync();
        var res = await _client.GetAsync("/api/reconciliation-review/pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(intruderId);
    }

    [Fact]
    public async Task GetPendingCount_returns_int()
    {
        var res = await _client.GetAsync("/api/reconciliation-review/pending/count");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var n = await res.Content.ReadFromJsonAsync<int>();
        n.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Confirm_returns_204_and_marks_confirmed()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsync($"/api/reconciliation-review/{stagedId}/confirm", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staged = await db.ImportStagedTransactions.FindAsync(stagedId);
        staged!.Status.Should().Be(StagedTransactionStatus.Confirmed);
    }

    [Fact]
    public async Task Confirm_returns_404_for_intruder_row()
    {
        var intruderId = await SeedIntruderStagedAsync();
        var res = await _client.PostAsync($"/api/reconciliation-review/{intruderId}/confirm", null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConfirmAll_returns_204_with_pending_rows_and_marks_all_confirmed()
    {
        var (_, _, stagedId1) = await SeedSentinelStagedAsync();
        var (_, _, stagedId2) = await SeedSentinelStagedAsync();

        var res = await _client.PostAsync("/api/reconciliation-review/confirm-all", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.ImportStagedTransactions
            .Where(s => s.Id == stagedId1 || s.Id == stagedId2)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(s => s.Status == StagedTransactionStatus.Confirmed);
    }

    [Fact]
    public async Task Dispute_returns_204_and_creates_disputed_transaction()
    {
        var (accountId, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsync($"/api/reconciliation-review/{stagedId}/dispute", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staged = await db.ImportStagedTransactions.FindAsync(stagedId);
        staged!.Status.Should().Be(StagedTransactionStatus.Disputed);
        // The dispute path creates a new Transaction for the disputed row.
        var newTx = await db.Transactions.FirstAsync(t => t.AccountId == accountId && t.Description == "imported-row");
        _transactionIds.Add(newTx.Id);
    }

    [Fact]
    public async Task Dispute_returns_404_for_intruder_row()
    {
        var intruderId = await SeedIntruderStagedAsync();
        var res = await _client.PostAsync($"/api/reconciliation-review/{intruderId}/dispute", null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
