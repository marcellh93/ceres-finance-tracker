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

/// <summary>
/// Multi-tenancy guard: every Create path on a user-owned entity must stamp
/// UserId = current user's id. In single-user mode the rows are still
/// retrievable, so the only way to catch a missing stamp is to query the row
/// directly after creation and verify the column. These tests exist precisely
/// to fail loudly if a future change forgets that line.
///
/// When real auth lands, these continue to work — the sentinel just becomes
/// the authenticated user's id, returned by ICurrentUserAccessor.
/// </summary>
[Collection("IntegrationTests")]
public class UserIdStampingTests : IAsyncLifetime
{
    private static readonly Guid Sentinel              = SingleUserAccessor.SentinelUserId;
    private static readonly Guid CashAccountId         = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CheckingAccountId     = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid CreditCardId          = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid HousingCategoryId     = new("20000000-0000-0000-0000-000000000008");
    private static readonly Guid SalaryCategoryId      = new("20000000-0000-0000-0000-000000000002");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _createdTransactionIds   = [];
    private readonly List<Guid> _createdTransferIds      = [];
    private readonly List<Guid> _createdBudgetIds        = [];
    private readonly List<Guid> _createdCategoryBudgetIds = [];
    private readonly List<Guid> _createdLiabilityIds     = [];
    private readonly List<Guid> _createdRecurringIds     = [];
    private readonly List<Guid> _createdCategoryIds      = [];

    public UserIdStampingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_createdTransactionIds.Count   > 0) await db.Transactions.Where(x => _createdTransactionIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdTransferIds.Count      > 0) await db.Transfers.Where(x => _createdTransferIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdLiabilityIds.Count     > 0) await db.LiabilityPayments.Where(x => _createdLiabilityIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdBudgetIds.Count        > 0) await db.Budgets.Where(x => _createdBudgetIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdCategoryBudgetIds.Count > 0) await db.CategoryBudgets.Where(x => _createdCategoryBudgetIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdRecurringIds.Count     > 0) await db.RecurringTransactions.Where(x => _createdRecurringIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (_createdCategoryIds.Count      > 0) await db.Categories.Where(x => _createdCategoryIds.Contains(x.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PostTransaction_stamps_UserId()
    {
        var res = await _client.PostAsJsonAsync("/api/transactions", new
        {
            date        = "2026-04-15",
            amount      = 12.34m,
            accountId   = CheckingAccountId,
            categoryId  = HousingCategoryId,
            description = "stamping-test-tx"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdTransactionIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task PostTransfer_stamps_UserId()
    {
        var res = await _client.PostAsJsonAsync("/api/transfers", new
        {
            date            = "2026-04-15",
            amount          = 50m,
            sourceAccountId = CheckingAccountId,
            destAccountId   = CashAccountId,
            description     = "stamping-test-transfer"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdTransferIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Transfers.AsNoTracking().FirstAsync(t => t.Id == id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task PostLiabilityPayment_stamps_UserId()
    {
        var res = await _client.PostAsJsonAsync("/api/liability-payments", new
        {
            date               = "2026-04-15",
            amount             = 100m,
            assetAccountId     = CheckingAccountId,
            liabilityAccountId = CreditCardId,
            description        = "stamping-test-liab"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdLiabilityIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.LiabilityPayments.AsNoTracking().FirstAsync(p => p.Id == id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task PostGoalBudget_stamps_UserId()
    {
        var res = await _client.PostAsJsonAsync("/api/goal-budgets", new
        {
            name         = $"Stamp-{Guid.NewGuid():N}",
            goalType     = "Spending",
            currencyId   = 1,
            targetAmount = 500m,
            startDate    = "2026-01-01"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdBudgetIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Budgets.AsNoTracking().FirstAsync(b => b.Id == id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task PostCategoryBudget_stamps_UserId()
    {
        var res = await _client.PostAsJsonAsync("/api/category-budgets", new
        {
            categoryId  = HousingCategoryId,
            currencyId  = 1,
            limitAmount = 250m
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdCategoryBudgetIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.CategoryBudgets.AsNoTracking().FirstAsync(cb => cb.Id == id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task RazorCategoryService_CreateAsync_stamps_UserId()
    {
        // Razor's CreateAsync (throwing) is still wired to the Razor controller. This
        // guards the path until the Razor controller is deleted in the SPA migration.
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.ICategoryService>();
        var created = await svc.CreateAsync(new ProjectCeres.ViewModels.CategoryCreateViewModel
        {
            Name           = $"StampedRazor-{Guid.NewGuid():N}",
            CategoryTypeId = 2,
            LifestyleTag   = null
        });
        _createdCategoryIds.Add(created.Id);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Categories.AsNoTracking().FirstAsync(c => c.Id == created.Id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task RecurringTransactionService_CreateAsync_stamps_UserId()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.IRecurringTransactionService>();
        var vm = new ProjectCeres.ViewModels.RecurringTransactionCreateViewModel
        {
            Name              = $"StampedRecurring-{Guid.NewGuid():N}",
            EstimatedAmount   = 100m,
            AccountId         = (Guid?)CheckingAccountId,
            CategoryId        = (Guid?)SalaryCategoryId,
            Frequency         = ProjectCeres.Models.Frequency.Monthly,
            DayOfPeriod       = 1,
            NextDueDate       = new DateOnly(2026, 6, 1),
            ReminderBehaviour = ProjectCeres.Models.ReminderBehaviour.SnapToCalendarDay
        };
        var created = await svc.CreateAsync(vm);
        _createdRecurringIds.Add(created.Id);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RecurringTransactions.AsNoTracking().FirstAsync(r => r.Id == created.Id);
        row.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task RecurringTransactionService_ConfirmAsync_stamps_UserId_on_created_transaction()
    {
        // ConfirmAsync converts a recurring template into a real Transaction. The created
        // Transaction must inherit the user's id.
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.IRecurringTransactionService>();

        var template = await svc.CreateAsync(new ProjectCeres.ViewModels.RecurringTransactionCreateViewModel
        {
            Name              = $"ConfirmSrc-{Guid.NewGuid():N}",
            EstimatedAmount   = 200m,
            AccountId         = (Guid?)CheckingAccountId,
            CategoryId        = (Guid?)SalaryCategoryId,
            Frequency         = ProjectCeres.Models.Frequency.Monthly,
            DayOfPeriod       = 1,
            NextDueDate       = new DateOnly(2026, 6, 1),
            ReminderBehaviour = ProjectCeres.Models.ReminderBehaviour.SnapToCalendarDay
        });
        _createdRecurringIds.Add(template.Id);

        var tx = await svc.ConfirmAsync(template.Id, new DateOnly(2026, 6, 1), 200m, "confirmed");
        _createdTransactionIds.Add(tx.Id);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        row.UserId.Should().Be(Sentinel);
    }
}
