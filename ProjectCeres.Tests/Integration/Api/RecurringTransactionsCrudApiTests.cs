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
public class RecurringTransactionsCrudApiTests : IAsyncLifetime
{
    private static readonly Guid CheckingAccountId = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SalaryCategoryId  = new("20000000-0000-0000-0000-000000000002");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _createdIds = [];
    private readonly List<Guid> _createdTransactionIds = [];

    public RecurringTransactionsCrudApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_createdTransactionIds.Count > 0)
            await db.Transactions.Where(t => _createdTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_createdIds.Count > 0)
            await db.RecurringTransactions.Where(r => _createdIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private object MakeCreateBody(string? name = null) => new
    {
        name              = name ?? $"Rent-{Guid.NewGuid():N}",
        estimatedAmount   = 1000m,
        accountId         = CheckingAccountId,
        categoryId        = SalaryCategoryId,
        frequency         = "Monthly",
        dayOfPeriod       = 1,
        nextDueDate       = "2026-06-01",
        reminderBehaviour = "SnapToCalendarDay"
    };

    private async Task<Guid> CreateOne()
    {
        var res = await _client.PostAsJsonAsync("/api/recurring-transactions", MakeCreateBody());
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _createdIds.Add(id);
        return id;
    }

    [Fact]
    public async Task Get_returns_active_reminders_by_default()
    {
        await CreateOne();
        var res = await _client.GetAsync("/api/recurring-transactions");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Should().OnlyContain(r => r.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Post_creates_reminder_with_owner()
    {
        var id = await CreateOne();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RecurringTransactions.AsNoTracking().FirstAsync(r => r.Id == id);
        row.UserId.Should().Be(SingleUserAccessor.SentinelUserId);
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Post_returns_422_on_invalid_account()
    {
        var res = await _client.PostAsJsonAsync("/api/recurring-transactions", new
        {
            name              = "Bad",
            estimatedAmount   = 100m,
            accountId         = Guid.NewGuid(),
            categoryId        = SalaryCategoryId,
            frequency         = "Monthly",
            nextDueDate       = "2026-06-01",
            reminderBehaviour = "SnapToCalendarDay"
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_ACCOUNT");
    }

    [Fact]
    public async Task Patch_updates_fields()
    {
        var id = await CreateOne();
        var res = await _client.PatchAsJsonAsync($"/api/recurring-transactions/{id}", new
        {
            name              = "Renamed",
            estimatedAmount   = 1500m,
            accountId         = CheckingAccountId,
            categoryId        = SalaryCategoryId,
            frequency         = "Monthly",
            dayOfPeriod       = 15,
            nextDueDate       = "2026-07-15",
            reminderBehaviour = "SnapToCalendarDay"
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("name").GetString().Should().Be("Renamed");
        dto.GetProperty("dayOfPeriod").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task Patch_returns_404_when_unknown_id()
    {
        var res = await _client.PatchAsJsonAsync($"/api/recurring-transactions/{Guid.NewGuid()}", new
        {
            name              = "x",
            estimatedAmount   = 100m,
            accountId         = CheckingAccountId,
            categoryId        = SalaryCategoryId,
            frequency         = "Monthly",
            nextDueDate       = "2026-06-01",
            reminderBehaviour = "SnapToCalendarDay"
        });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archive_deactivates()
    {
        var id = await CreateOne();
        var res = await _client.PatchAsync($"/api/recurring-transactions/{id}/archive", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.RecurringTransactions.FindAsync(id);
        row!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_creates_transaction_and_advances_due_date()
    {
        var id = await CreateOne();
        var res = await _client.PostAsJsonAsync($"/api/recurring-transactions/{id}/confirm", new
        {
            date        = "2026-06-01",
            amount      = 1000m,
            description = "confirmed-payment"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var txId = body.GetProperty("transactionId").GetGuid();
        _createdTransactionIds.Add(txId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tx = await db.Transactions.FindAsync(txId);
        tx!.UserId.Should().Be(SingleUserAccessor.SentinelUserId);
        tx.Description.Should().Be("confirmed-payment");

        var reminder = await db.RecurringTransactions.FindAsync(id);
        reminder!.NextDueDate.Should().NotBe(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public async Task Confirm_returns_404_when_unknown_id()
    {
        var res = await _client.PostAsJsonAsync($"/api/recurring-transactions/{Guid.NewGuid()}/confirm", new
        {
            date   = "2026-06-01",
            amount = 100m
        });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dismiss_advances_due_date_without_creating_transaction()
    {
        var id = await CreateOne();
        var before = await _client.GetFromJsonAsync<JsonElement>($"/api/recurring-transactions/{id}");
        var beforeDate = before.GetProperty("nextDueDate").GetDateTime();

        var res = await _client.PostAsync($"/api/recurring-transactions/{id}/dismiss", null);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetFromJsonAsync<JsonElement>($"/api/recurring-transactions/{id}");
        after.GetProperty("nextDueDate").GetDateTime().Should().NotBe(beforeDate);
    }

    [Fact]
    public async Task Upcoming_returns_active_reminders_within_window()
    {
        var id = await CreateOne();
        var res = await _client.GetAsync("/api/recurring-transactions/upcoming?days=10000");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task GetById_returns_404_for_intruder_row()
    {
        var intruderId = Guid.NewGuid();
        var intruderUser = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecurringTransactions.Add(new RecurringTransaction
            {
                Id              = intruderId,
                UserId          = intruderUser,
                Name            = "intruder-recurring",
                EstimatedAmount = 1m,
                AccountId       = CheckingAccountId,
                CategoryId      = SalaryCategoryId,
                Frequency       = Frequency.Monthly,
                NextDueDate     = new DateOnly(2026, 6, 1),
                IsActive        = true,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var res = await _client.GetAsync($"/api/recurring-transactions/{intruderId}");
            res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RecurringTransactions.Where(r => r.Id == intruderId).ExecuteDeleteAsync();
        }
    }
}
