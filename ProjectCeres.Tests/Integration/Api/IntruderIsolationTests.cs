using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Services.Reports;

namespace ProjectCeres.Tests.Integration.Api;

/// <summary>
/// Multi-tenancy IDOR guard: data owned by another user (here represented by an
/// "intruder" UserId) must never appear in queries served to the current user.
///
/// In single-user mode every legitimate row is stamped with the sentinel UserId.
/// We seed an extra row with a freshly-generated random UserId — that row stands
/// in for "User B's data." If a read endpoint returns it, the multi-tenancy
/// boundary leaks. After Batch B's filter additions, every test here passes
/// because the queries are scoped to the current user's id.
///
/// When real auth lands these tests continue to work — replace the seed
/// "intruder UserId" with another authenticated user and the same shape holds.
/// </summary>
[Collection("IntegrationParallel4")]
public class IntruderIsolationTests : IntegrationTestBase<Bucket4Factory>, IAsyncLifetime
{
    private static readonly Guid Sentinel              = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid CashAccountId         = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CheckingAccountId     = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid CreditCardId          = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid HousingCategoryId     = new("20000000-0000-0000-0000-000000000008");
    private static readonly Guid SalaryCategoryId      = new("20000000-0000-0000-0000-000000000002");

    private readonly Bucket4Factory _factory;
    private readonly HttpClient _client;
    private readonly Guid _intruderUserId = Guid.NewGuid();
    private readonly List<Guid> _intruderAccountIds            = [];
    private readonly List<Guid> _intruderCategoryIds           = [];
    private readonly List<Guid> _intruderTransactionIds        = [];
    private readonly List<Guid> _intruderTransferIds           = [];
    private readonly List<Guid> _intruderLiabilityPaymentIds   = [];
    private readonly List<Guid> _intruderBudgetIds             = [];
    private readonly List<Guid> _intruderCategoryBudgetIds     = [];
    private readonly List<Guid> _intruderRecurringIds          = [];

    public IntruderIsolationTests(Bucket4Factory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (_intruderTransactionIds.Count > 0)      await db.Transactions.Where(t => _intruderTransactionIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_intruderTransferIds.Count > 0)         await db.Transfers.Where(t => _intruderTransferIds.Contains(t.Id)).ExecuteDeleteAsync();
        if (_intruderLiabilityPaymentIds.Count > 0) await db.LiabilityPayments.Where(p => _intruderLiabilityPaymentIds.Contains(p.Id)).ExecuteDeleteAsync();
        if (_intruderBudgetIds.Count > 0)           await db.Budgets.Where(b => _intruderBudgetIds.Contains(b.Id)).ExecuteDeleteAsync();
        if (_intruderCategoryBudgetIds.Count > 0)   await db.CategoryBudgets.Where(cb => _intruderCategoryBudgetIds.Contains(cb.Id)).ExecuteDeleteAsync();
        if (_intruderRecurringIds.Count > 0)        await db.RecurringTransactions.Where(r => _intruderRecurringIds.Contains(r.Id)).ExecuteDeleteAsync();
        if (_intruderAccountIds.Count > 0)          await db.Accounts.Where(a => _intruderAccountIds.Contains(a.Id)).ExecuteDeleteAsync();
        if (_intruderCategoryIds.Count > 0)         await db.Categories.Where(c => _intruderCategoryIds.Contains(c.Id)).ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers — seed rows owned by a different user.
    // -------------------------------------------------------------------------

    private async Task<Account> SeedIntruderAccountAsync(string namePrefix = "intruder")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = new Account
        {
            Id            = Guid.NewGuid(),
            Name          = $"{namePrefix}-{Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true,
            UserId        = _intruderUserId,
        };
        db.Accounts.Add(a);
        await db.SaveChangesAsync();
        _intruderAccountIds.Add(a.Id);
        return a;
    }

    private async Task<Transaction> SeedIntruderTransactionAsync(Guid accountId, Guid categoryId, decimal amount = 99m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = new Transaction
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            Date        = new DateOnly(2026, 4, 15),
            Amount      = amount,
            AccountId   = accountId,
            CategoryId  = categoryId,
            Description = "intruder-tx",
            CreatedAt   = DateTime.UtcNow,
        };
        db.Transactions.Add(t);
        await db.SaveChangesAsync();
        _intruderTransactionIds.Add(t.Id);
        return t;
    }

    private async Task<Transfer> SeedIntruderTransferAsync(Guid sourceAccountId, Guid destAccountId, decimal amount = 50m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = new Transfer
        {
            Id              = Guid.NewGuid(),
            UserId          = _intruderUserId,
            Date            = new DateOnly(2026, 4, 15),
            Amount          = amount,
            SourceAccountId = sourceAccountId,
            DestAccountId   = destAccountId,
            Description     = "intruder-transfer",
            CreatedAt       = DateTime.UtcNow,
        };
        db.Transfers.Add(t);
        await db.SaveChangesAsync();
        _intruderTransferIds.Add(t.Id);
        return t;
    }

    private async Task<LiabilityPayment> SeedIntruderLiabilityAsync(Guid assetAccountId, Guid liabilityAccountId, decimal amount = 75m)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var p = new LiabilityPayment
        {
            Id                 = Guid.NewGuid(),
            UserId             = _intruderUserId,
            Date               = new DateOnly(2026, 4, 15),
            Amount             = amount,
            AssetAccountId     = assetAccountId,
            LiabilityAccountId = liabilityAccountId,
            Description        = "intruder-liability",
            CreatedAt          = DateTime.UtcNow,
        };
        db.LiabilityPayments.Add(p);
        await db.SaveChangesAsync();
        _intruderLiabilityPaymentIds.Add(p.Id);
        return p;
    }

    private async Task<Budget> SeedIntruderBudgetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var b = new Budget
        {
            Id           = Guid.NewGuid(),
            UserId       = _intruderUserId,
            Name         = $"intruder-budget-{Guid.NewGuid():N}",
            TargetAmount = 1000m,
            CurrencyId   = 1,
            StartDate    = new DateOnly(2026, 1, 1),
            GoalType     = "Spending",
            IsActive     = true,
        };
        db.Budgets.Add(b);
        await db.SaveChangesAsync();
        _intruderBudgetIds.Add(b.Id);
        return b;
    }

    private async Task<CategoryBudget> SeedIntruderCategoryBudgetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cb = new CategoryBudget
        {
            Id          = Guid.NewGuid(),
            UserId      = _intruderUserId,
            CategoryId  = HousingCategoryId,
            CurrencyId  = 1,
            LimitAmount = 500m,
            IsActive    = true,
        };
        db.CategoryBudgets.Add(cb);
        await db.SaveChangesAsync();
        _intruderCategoryBudgetIds.Add(cb.Id);
        return cb;
    }

    private async Task<RecurringTransaction> SeedIntruderRecurringAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var r = new RecurringTransaction
        {
            Id              = Guid.NewGuid(),
            UserId          = _intruderUserId,
            Name            = $"intruder-recurring-{Guid.NewGuid():N}",
            EstimatedAmount = 100m,
            AccountId       = CheckingAccountId,
            CategoryId      = SalaryCategoryId,
            Frequency       = Frequency.Monthly,
            DayOfPeriod     = 1,
            NextDueDate     = new DateOnly(2026, 6, 1),
            IsActive        = true,
        };
        db.RecurringTransactions.Add(r);
        await db.SaveChangesAsync();
        _intruderRecurringIds.Add(r.Id);
        return r;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transaction_GetById_returns_404_for_intruder_row()
    {
        var account = await SeedIntruderAccountAsync();
        var tx = await SeedIntruderTransactionAsync(account.Id, HousingCategoryId);

        var res = await _client.GetAsync($"/api/transactions/{tx.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transfer_GetById_returns_404_for_intruder_row()
    {
        var src = await SeedIntruderAccountAsync("intruder-src");
        var dst = await SeedIntruderAccountAsync("intruder-dst");
        var tr = await SeedIntruderTransferAsync(src.Id, dst.Id);

        var res = await _client.GetAsync($"/api/transfers/{tr.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LiabilityPayment_GetById_returns_404_for_intruder_row()
    {
        var asset = await SeedIntruderAccountAsync("intruder-asset");
        // Build an intruder liability account directly.
        Guid liabilityAccountId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var liab = new Account
            {
                Id = Guid.NewGuid(), Name = $"intruder-liab-{Guid.NewGuid():N}",
                AccountTypeId = 2, CurrencyId = 1, IsActive = true, UserId = _intruderUserId
            };
            db.Accounts.Add(liab);
            await db.SaveChangesAsync();
            _intruderAccountIds.Add(liab.Id);
            liabilityAccountId = liab.Id;
        }
        var p = await SeedIntruderLiabilityAsync(asset.Id, liabilityAccountId);

        var res = await _client.GetAsync($"/api/liability-payments/{p.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Movements_list_excludes_intruder_rows()
    {
        var account = await SeedIntruderAccountAsync();
        var tx = await SeedIntruderTransactionAsync(account.Id, HousingCategoryId);

        var res = await _client.GetAsync("/api/movements");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(tx.Id.ToString());
    }

    [Fact]
    public async Task GoalBudgets_list_excludes_intruder_rows()
    {
        var b = await SeedIntruderBudgetAsync();

        var res = await _client.GetAsync("/api/goal-budgets");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(b.Id);
    }

    [Fact]
    public async Task GoalBudget_GetById_returns_404_for_intruder_row()
    {
        var b = await SeedIntruderBudgetAsync();

        var res = await _client.GetAsync($"/api/goal-budgets/{b.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CategoryBudgets_list_excludes_intruder_rows()
    {
        var cb = await SeedIntruderCategoryBudgetAsync();

        var res = await _client.GetAsync("/api/category-budgets");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(cb.Id);
    }

    [Fact]
    public async Task CategoryBudget_GetById_returns_404_for_intruder_row()
    {
        var cb = await SeedIntruderCategoryBudgetAsync();

        var res = await _client.GetAsync($"/api/category-budgets/{cb.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_summary_does_not_include_intruder_transactions()
    {
        // Stamp a unique amount so we can detect leakage by value.
        const decimal uniqueAmount = 13579.42m;

        var account = await SeedIntruderAccountAsync();
        await SeedIntruderTransactionAsync(account.Id, HousingCategoryId, amount: uniqueAmount);

        var res = await _client.GetAsync("/api/dashboard/summary");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(uniqueAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Dashboard_account_balances_does_not_include_intruder_account()
    {
        var account = await SeedIntruderAccountAsync("intruder-balances");

        var res = await _client.GetAsync("/api/dashboard/account-balances");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain(account.Id.ToString());
        body.Should().NotContain(account.Name);
    }

    [Fact]
    public async Task RecurringTransactionService_GetAllAsync_excludes_intruder_rows()
    {
        var r = await SeedIntruderRecurringAsync();

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRecurringTransactionService>();
        var rows = await svc.GetAllAsync(includeInactive: true);
        rows.Select(x => x.Id).Should().NotContain(r.Id);
    }

    [Fact]
    public async Task BudgetService_GetByIdAsync_returns_null_for_intruder_row()
    {
        var b = await SeedIntruderBudgetAsync();

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBudgetService>();
        var found = await svc.GetByIdAsync(b.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task ImportProfileService_GetAllActiveAsync_excludes_intruder_profile()
    {
        Guid intruderProfileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = new ImportProfile
            {
                Id             = Guid.NewGuid(),
                UserId         = _intruderUserId,
                Name           = $"intruder-profile-{Guid.NewGuid():N}",
                ColumnMappings = "{}",
                Format         = ImportFormat.Csv,
                CreatedAt      = DateTime.UtcNow,
            };
            db.ImportProfiles.Add(profile);
            await db.SaveChangesAsync();
            intruderProfileId = profile.Id;
        }

        try
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IImportProfileService>();
            var rows = await svc.GetAllActiveAsync();
            rows.Select(r => r.Id).Should().NotContain(intruderProfileId);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ImportProfiles.Where(p => p.Id == intruderProfileId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ImportStagedTransactionService_GetPendingAsync_excludes_intruder_rows()
    {
        // Need a real account for the FK; create one owned by intruder.
        var intruderAccount = await SeedIntruderAccountAsync("intruder-staged");
        Guid intruderStagedId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staged = new ImportStagedTransaction
            {
                Id             = Guid.NewGuid(),
                UserId         = _intruderUserId,
                ImportedAt     = DateTime.UtcNow,
                AccountId      = intruderAccount.Id,
                RawDate        = new DateOnly(2026, 4, 15),
                RawAmount      = 50m,
                RawDescription = "intruder-staged",
                Status         = StagedTransactionStatus.Pending,
            };
            db.ImportStagedTransactions.Add(staged);
            await db.SaveChangesAsync();
            intruderStagedId = staged.Id;
        }

        try
        {
            using var scope = _factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IImportStagedTransactionService>();
            var pending = await svc.GetPendingAsync();
            pending.Select(s => s.Id).Should().NotContain(intruderStagedId);

            var count = await svc.GetPendingCountAsync();
            count.Should().Be(pending.Count); // sanity: count and list agree
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ImportStagedTransactions.Where(s => s.Id == intruderStagedId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ReportService_NetWorth_does_not_include_intruder_account_balance()
    {
        // Liability + asset accounts are both summed by the net-worth report. A leaked
        // intruder account would skew the totals.
        var account = await SeedIntruderAccountAsync("intruder-networth");
        // Anchor a known opening-balance amount so leakage would be obvious.
        const decimal uniqueOpeningBalance = 24681m;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Transactions.Add(new Transaction
            {
                Id          = Guid.NewGuid(),
                UserId      = _intruderUserId,
                Date        = new DateOnly(2026, 1, 1),
                Amount      = uniqueOpeningBalance,
                AccountId   = account.Id,
                CategoryId  = new Guid("20000000-0000-0000-0000-000000000001"), // Opening Balance system category
                Description = "intruder-opening-balance",
                CreatedAt   = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var scope2 = _factory.Services.CreateScope();
        var generator = scope2.ServiceProvider.GetRequiredService<NetWorthGenerator>();
        var dto = await generator.GenerateAsync(new ReportParameters(CurrencyId: 1));
        var serialized = System.Text.Json.JsonSerializer.Serialize(dto);
        serialized.Should().NotContain(account.Name);
        serialized.Should().NotContain(uniqueOpeningBalance.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Cleanup the extra opening-balance tx we added inline (its account cleanup is covered by _intruderAccountIds).
        using var cleanupScope = _factory.Services.CreateScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await cleanupDb.Transactions.Where(t => t.Description == "intruder-opening-balance").ExecuteDeleteAsync();
    }
}
