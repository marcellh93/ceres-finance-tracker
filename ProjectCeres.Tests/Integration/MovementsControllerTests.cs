using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WebApplicationFactory tests for Stage 2.5.2:
/// — GET /Movements returns 200 OK
/// — returnUrl routing: Edit + Delete on TransactionsController and TransfersController
///   redirect to returnUrl when present and local, or fall back to own Index.
///
/// The factory overrides the connection string to project_ceres_test so these tests
/// never touch the dev database. Seeded rows are deleted in DisposeAsync.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset
///   CurrencyId    1 = EUR
///   CategoryId 20000000-0000-0000-0000-000000000008 = Housing (Expense)
/// </summary>
[Collection("IntegrationTests")]
public class MovementsControllerTests : IAsyncLifetime
{
    private static readonly Guid HousingCategoryId = new("20000000-0000-0000-0000-000000000008");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];
    private readonly List<Guid> _seededTransactionIds = [];
    private readonly List<Guid> _seededTransferIds = [];

    public MovementsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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

    private async Task<Guid> SeedAssetAccountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var account = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"WAF Asset {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        _seededAccountIds.Add(account.Id);
        return account.Id;
    }

    private async Task<Guid> SeedTransactionAsync(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var txn = new Transaction
        {
            Id         = Guid.NewGuid(),
            Date       = new DateOnly(2025, 6, 1),
            Amount     = 100m,
            AccountId  = accountId,
            CategoryId = HousingCategoryId,
            IsCleared  = false,
            CreatedAt  = DateTime.UtcNow
        };
        db.Transactions.Add(txn);
        await db.SaveChangesAsync();
        _seededTransactionIds.Add(txn.Id);
        return txn.Id;
    }

    private async Task<Guid> SeedTransferAsync(Guid sourceId, Guid destId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var transfer = new Transfer
        {
            Id              = Guid.NewGuid(),
            Date            = new DateOnly(2025, 6, 1),
            Amount          = 50m,
            SourceAccountId = sourceId,
            DestAccountId   = destId,
            IsCleared       = false,
            CreatedAt       = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();
        _seededTransferIds.Add(transfer.Id);
        return transfer.Id;
    }

    // -------------------------------------------------------------------------
    // GET /Movements — 200 OK
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMovements_Returns200Ok_WithMovementsContent()
    {
        var response = await _client.GetAsync("/Movements");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Movements", because: "the Movements index page must render its own content");
    }

    // -------------------------------------------------------------------------
    // Transaction Edit — returnUrl routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransactionEdit_Post_WithReturnUrl_RedirectsToReturnUrl()
    {
        var accountId = await SeedAssetAccountAsync();
        var txnId     = await SeedTransactionAsync(accountId);

        var form = new Dictionary<string, string>
        {
            ["Id"]              = txnId.ToString(),
            ["TransactionType"] = "Regular",
            ["Date"]            = "2025-06-01",
            ["AccountId"]       = accountId.ToString(),
            ["CategoryId"]      = HousingCategoryId.ToString(),
            ["Amount"]          = "100",
            ["Description"]     = "Updated",
            ["returnUrl"]       = "/Movements",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transactions/Edit/" + txnId)
        };

        var response = await _client.PostAsync(
            "/Transactions/Edit/" + txnId,
            new FormUrlEncodedContent(form));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Movements");
    }

    [Fact]
    public async Task TransactionEdit_Post_WithoutReturnUrl_RedirectsToTransactionsIndex()
    {
        var accountId = await SeedAssetAccountAsync();
        var txnId     = await SeedTransactionAsync(accountId);

        var form = new Dictionary<string, string>
        {
            ["Id"]              = txnId.ToString(),
            ["TransactionType"] = "Regular",
            ["Date"]            = "2025-06-01",
            ["AccountId"]       = accountId.ToString(),
            ["CategoryId"]      = HousingCategoryId.ToString(),
            ["Amount"]          = "100",
            ["Description"]     = "Updated",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transactions/Edit/" + txnId)
        };

        var response = await _client.PostAsync(
            "/Transactions/Edit/" + txnId,
            new FormUrlEncodedContent(form));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Transactions");
    }

    // -------------------------------------------------------------------------
    // Transaction Delete — returnUrl routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransactionDelete_Post_WithReturnUrl_RedirectsToReturnUrl()
    {
        var accountId = await SeedAssetAccountAsync();
        var txnId     = await SeedTransactionAsync(accountId);

        var form = new Dictionary<string, string>
        {
            ["id"]        = txnId.ToString(),
            ["returnUrl"] = "/Movements",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transactions/Delete/" + txnId)
        };

        var response = await _client.PostAsync(
            "/Transactions/Delete/" + txnId,
            new FormUrlEncodedContent(form));

        // Row is deleted by the action — remove from cleanup list to avoid a no-op delete
        _seededTransactionIds.Remove(txnId);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Movements");
    }

    [Fact]
    public async Task TransactionDelete_Post_WithoutReturnUrl_RedirectsToTransactionsIndex()
    {
        var accountId = await SeedAssetAccountAsync();
        var txnId     = await SeedTransactionAsync(accountId);

        var form = new Dictionary<string, string>
        {
            ["id"] = txnId.ToString(),
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transactions/Delete/" + txnId)
        };

        var response = await _client.PostAsync(
            "/Transactions/Delete/" + txnId,
            new FormUrlEncodedContent(form));

        _seededTransactionIds.Remove(txnId);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Transactions");
    }

    // -------------------------------------------------------------------------
    // Transfer Edit — returnUrl routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransferEdit_Post_WithReturnUrl_RedirectsToReturnUrl()
    {
        var sourceId   = await SeedAssetAccountAsync();
        var destId     = await SeedAssetAccountAsync();
        var transferId = await SeedTransferAsync(sourceId, destId);

        var form = new Dictionary<string, string>
        {
            ["Id"]              = transferId.ToString(),
            ["Date"]            = "2025-06-01",
            ["SourceAccountId"] = sourceId.ToString(),
            ["DestAccountId"]   = destId.ToString(),
            ["Amount"]          = "50",
            ["Description"]     = "Updated",
            ["returnUrl"]       = "/Movements",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transfers/Edit/" + transferId)
        };

        var response = await _client.PostAsync(
            "/Transfers/Edit/" + transferId,
            new FormUrlEncodedContent(form));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Movements");
    }

    [Fact]
    public async Task TransferEdit_Post_WithoutReturnUrl_RedirectsToTransfersIndex()
    {
        var sourceId   = await SeedAssetAccountAsync();
        var destId     = await SeedAssetAccountAsync();
        var transferId = await SeedTransferAsync(sourceId, destId);

        var form = new Dictionary<string, string>
        {
            ["Id"]              = transferId.ToString(),
            ["Date"]            = "2025-06-01",
            ["SourceAccountId"] = sourceId.ToString(),
            ["DestAccountId"]   = destId.ToString(),
            ["Amount"]          = "50",
            ["Description"]     = "Updated",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transfers/Edit/" + transferId)
        };

        var response = await _client.PostAsync(
            "/Transfers/Edit/" + transferId,
            new FormUrlEncodedContent(form));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Transfers");
    }

    // -------------------------------------------------------------------------
    // Transfer Delete — returnUrl routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransferDelete_Post_WithReturnUrl_RedirectsToReturnUrl()
    {
        var sourceId   = await SeedAssetAccountAsync();
        var destId     = await SeedAssetAccountAsync();
        var transferId = await SeedTransferAsync(sourceId, destId);

        var form = new Dictionary<string, string>
        {
            ["id"]        = transferId.ToString(),
            ["returnUrl"] = "/Movements",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transfers/Delete/" + transferId)
        };

        var response = await _client.PostAsync(
            "/Transfers/Delete/" + transferId,
            new FormUrlEncodedContent(form));

        _seededTransferIds.Remove(transferId);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Movements");
    }

    [Fact]
    public async Task TransferDelete_Post_WithoutReturnUrl_RedirectsToTransfersIndex()
    {
        var sourceId   = await SeedAssetAccountAsync();
        var destId     = await SeedAssetAccountAsync();
        var transferId = await SeedTransferAsync(sourceId, destId);

        var form = new Dictionary<string, string>
        {
            ["id"] = transferId.ToString(),
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transfers/Delete/" + transferId)
        };

        var response = await _client.PostAsync(
            "/Transfers/Delete/" + transferId,
            new FormUrlEncodedContent(form));

        _seededTransferIds.Remove(transferId);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Transfers");
    }

    // -------------------------------------------------------------------------
    // Open redirect safety — external returnUrl must be rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TransactionEdit_Post_WithExternalReturnUrl_FallsBackToTransactionsIndex()
    {
        var accountId = await SeedAssetAccountAsync();
        var txnId     = await SeedTransactionAsync(accountId);

        var form = new Dictionary<string, string>
        {
            ["Id"]              = txnId.ToString(),
            ["TransactionType"] = "Regular",
            ["Date"]            = "2025-06-01",
            ["AccountId"]       = accountId.ToString(),
            ["CategoryId"]      = HousingCategoryId.ToString(),
            ["Amount"]          = "100",
            ["Description"]     = "Updated",
            ["returnUrl"]       = "https://evil.com/steal",
            ["__RequestVerificationToken"] = await GetAntiForgeryTokenAsync("/Transactions/Edit/" + txnId)
        };

        var response = await _client.PostAsync(
            "/Transactions/Edit/" + txnId,
            new FormUrlEncodedContent(form));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Be("/Transactions");
    }

    // -------------------------------------------------------------------------
    // Anti-forgery token helper
    // -------------------------------------------------------------------------

    private async Task<string> GetAntiForgeryTokenAsync(string path)
    {
        var page = await _client.GetStringAsync(path);
        var match = System.Text.RegularExpressions.Regex.Match(
            page,
            @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
