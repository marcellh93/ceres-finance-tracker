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

[Collection("IntegrationParallel2")]
public class TransferReviewApiTests : IntegrationTestBase<Bucket2Factory>, IAsyncLifetime
{
    private readonly Bucket2Factory _factory;
    private readonly HttpClient _client;
    private readonly Guid _intruderUserId = Guid.NewGuid();
    private readonly List<Guid> _stagedIds = [];
    private readonly List<Guid> _accountIds = [];
    private readonly List<Guid> _transferIds = [];
    private readonly List<Guid> _transactionIds = [];

    public TransferReviewApiTests(Bucket2Factory factory, Bucket2Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_stagedIds.Count > 0)       await db.ImportStagedTransfers.Where(s => _stagedIds.Contains(s.Id)).ExecuteDeleteAsync();
        if (_transferIds.Count > 0)     await db.Transfers.Where(t => _transferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_transactionIds.Count > 0)  await db.Transactions.Where(t => _transactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_accountIds.Count > 0)      await db.Accounts.Where(a => _accountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    private async Task<(Guid SourceAccountId, Guid OtherAccountId, Guid StagedId)> SeedSentinelStagedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var src   = new Account { Id = Guid.NewGuid(), Name = $"TR-Src-{Guid.NewGuid():N}",   AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var other = new Account { Id = Guid.NewGuid(), Name = $"TR-Other-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true };
        var staged = new ImportStagedTransfer
        {
            Id             = Guid.NewGuid(),
            ImportedAt     = DateTime.UtcNow,
            AccountId      = src.Id,
            RawDate        = new DateOnly(2026, 4, 15),
            RawAmount      = -50m,
            RawDescription = "transfer-test",
            Status         = StagedTransferStatus.Pending,
        };
        db.Accounts.AddRange(src, other);
        db.ImportStagedTransfers.Add(staged);
        await db.SaveChangesAsync();
        _accountIds.Add(src.Id); _accountIds.Add(other.Id); _stagedIds.Add(staged.Id);
        return (src.Id, other.Id, staged.Id);
    }

    private async Task<Guid> SeedIntruderStagedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), Name = $"TR-Intruder-{Guid.NewGuid():N}", AccountTypeId = 1, CurrencyId = 1, IsActive = true, UserId = _intruderUserId };
        var staged = new ImportStagedTransfer
        {
            Id             = Guid.NewGuid(),
            UserId         = _intruderUserId,
            ImportedAt     = DateTime.UtcNow,
            AccountId      = account.Id,
            RawDate        = new DateOnly(2026, 4, 15),
            RawAmount      = -25m,
            RawDescription = "intruder-staged",
            Status         = StagedTransferStatus.Pending,
        };
        db.Accounts.Add(account);
        db.ImportStagedTransfers.Add(staged);
        await db.SaveChangesAsync();
        _accountIds.Add(account.Id); _stagedIds.Add(staged.Id);
        return staged.Id;
    }

    [Fact]
    public async Task GetPending_returns_staged_transfer()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.GetAsync("/api/transfer-review/pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(stagedId);
    }

    [Fact]
    public async Task GetPending_includes_account_currency_code_and_symbol()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.GetAsync("/api/transfer-review/pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        var row  = rows!.First(r => r.GetProperty("id").GetGuid() == stagedId);
        row.GetProperty("accountCurrencyCode").GetString().Should().Be("EUR");
        row.GetProperty("accountCurrencySymbol").GetString().Should().Be("€");
    }

    [Fact]
    public async Task GetPending_excludes_intruder_rows()
    {
        var intruderId = await SeedIntruderStagedAsync();
        var res = await _client.GetAsync("/api/transfer-review/pending");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(intruderId);
    }

    [Fact]
    public async Task GetPendingCount_returns_int()
    {
        var res = await _client.GetAsync("/api/transfer-review/pending/count");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var n = await res.Content.ReadFromJsonAsync<int>();
        n.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task LinkToExisting_returns_204_and_creates_transfer()
    {
        var (_, otherAccountId, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsJsonAsync($"/api/transfer-review/{stagedId}/link-to-existing",
            new { otherAccountId });
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId);
        staged!.Status.Should().Be(StagedTransferStatus.Linked);
        var transfer = await db.Transfers.FirstAsync(t => t.Description == "transfer-test");
        _transferIds.Add(transfer.Id);
    }

    [Fact]
    public async Task LinkToExisting_returns_404_for_intruder_staged_row()
    {
        var intruderId = await SeedIntruderStagedAsync();
        // Use a sentinel-owned account as the "other" — the staged row itself is the intruder.
        var (_, otherAccountId, _) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsJsonAsync($"/api/transfer-review/{intruderId}/link-to-existing",
            new { otherAccountId });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAsTransfer_returns_204_and_creates_transfer()
    {
        var (_, otherAccountId, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsJsonAsync($"/api/transfer-review/{stagedId}/create-as-transfer",
            new { otherAccountId });
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId);
        staged!.Status.Should().Be(StagedTransferStatus.CreatedAsTransfer);
        var transfer = await db.Transfers.FirstAsync(t => t.Description == "transfer-test");
        _transferIds.Add(transfer.Id);
    }

    [Fact]
    public async Task CreateAsTransfer_returns_422_when_other_account_unknown()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsJsonAsync($"/api/transfer-review/{stagedId}/create-as-transfer",
            new { otherAccountId = Guid.NewGuid() });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_ACCOUNT");
    }

    [Fact]
    public async Task DismissAsTransaction_returns_204_and_creates_transaction()
    {
        var (sourceId, _, stagedId) = await SeedSentinelStagedAsync();
        var res = await _client.PostAsync($"/api/transfer-review/{stagedId}/dismiss-as-transaction", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staged = await db.ImportStagedTransfers.FindAsync(stagedId);
        staged!.Status.Should().Be(StagedTransferStatus.DismissedAsTransaction);
        var tx = await db.Transactions.FirstAsync(t => t.AccountId == sourceId && t.Description == "transfer-test");
        _transactionIds.Add(tx.Id);

        // Cleanup: the dismiss path also creates an exclusion row; remove it before disposal so
        // the unique (UserId, DescriptionPattern) index doesn't trip on retries.
        await db.ImportTransferExclusions.Where(e => e.DescriptionPattern == "transfer-test").ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DismissAsTransaction_does_not_duplicate_exclusion_when_pattern_already_exists()
    {
        var (_, _, stagedId) = await SeedSentinelStagedAsync();

        // Pre-seed an exclusion row whose pattern matches the staged transfer's RawDescription.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.ImportTransferExclusions.Add(new ImportTransferExclusion
            {
                Id                 = Guid.NewGuid(),
                DescriptionPattern = "transfer-test",
                CreatedAt          = DateTime.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        try
        {
            var res = await _client.PostAsync($"/api/transfer-review/{stagedId}/dismiss-as-transaction", null);
            res.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staged = await db.ImportStagedTransfers.FindAsync(stagedId);
            staged!.Status.Should().Be(StagedTransferStatus.DismissedAsTransaction);

            // Track the created transaction for cleanup.
            var tx = await db.Transactions.FirstAsync(t => t.Description == "transfer-test");
            _transactionIds.Add(tx.Id);

            // The dedup branch must NOT have added a second exclusion row.
            var count = await db.ImportTransferExclusions
                .CountAsync(e => e.DescriptionPattern == "transfer-test");
            count.Should().Be(1);
        }
        finally
        {
            // Cleanup the seeded exclusion so the unique (UserId, DescriptionPattern) index
            // doesn't trip on retries or other tests reusing this description.
            using var cleanupScope = _factory.Services.CreateScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await cleanupDb.ImportTransferExclusions
                .Where(e => e.DescriptionPattern == "transfer-test")
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task DismissAsTransaction_returns_404_for_intruder_staged_row()
    {
        var intruderId = await SeedIntruderStagedAsync();
        var res = await _client.PostAsync($"/api/transfer-review/{intruderId}/dismiss-as-transaction", null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
