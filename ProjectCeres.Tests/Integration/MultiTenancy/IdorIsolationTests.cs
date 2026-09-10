using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.MultiTenancy;

/// <summary>
/// IDOR (Insecure Direct Object Reference) integration tests.
///
/// Asserts that User B, authenticated via the real cookie pipeline, cannot read,
/// modify, or delete User A's resources. Every assertion expects HTTP 404 — not 403
/// — because returning 403 leaks the existence of the row.
///
/// Also contains a negative-assertion test that proves the EF global query filter
/// alone catches a cross-tenant leak even when the service-layer .Owned(user) chain
/// is bypassed entirely.
/// </summary>
[Collection("IntegrationParallel1")]
public class IdorIsolationTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    private const string EmailSuffix = "@idor-test.local";

    // Seeded for User A — populated in InitializeAsync.
    private ApplicationUser _userA = null!;
    private ApplicationUser _userB = null!;
    private Guid _aAccountId;
    private Guid _aTransactionId;
    private Guid _aBudgetId;
    private Guid _aCategoryBudgetId;
    private Guid _aRecurringTxId;
    private Guid _aImportProfileId;

    // B's session cookie — set after login in InitializeAsync.
    private string _bSessionCookie = null!;

    public IdorIsolationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    // ---------------------------------------------------------------------------
    // Setup
    // ---------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _userA = await AuthTestFixture.RegisterUserAsync(_factory, $"a-{Guid.NewGuid():N}{EmailSuffix}");
        _userB = await AuthTestFixture.RegisterUserAsync(_factory, $"b-{Guid.NewGuid():N}{EmailSuffix}");

        // Seed User A's data directly via DbContext so we control the IDs.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Pick an Income category seeded for User A (CategoryTypeId == 1).
            var incomeCategory = await db.Categories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.UserId == _userA.Id && c.CategoryTypeId == 1);

            // Pick an Expense category seeded for User A (CategoryTypeId == 2).
            var expenseCategory = await db.Categories
                .IgnoreQueryFilters()
                .FirstAsync(c => c.UserId == _userA.Id && c.CategoryTypeId == 2 && !c.IsReserved);

            // Account — Asset (AccountTypeId=1), EUR (CurrencyId=1).
            _aAccountId = Guid.NewGuid();
            db.Accounts.Add(new Account
            {
                Id            = _aAccountId,
                Name          = "IDOR-A-Account",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = _userA.Id,
            });

            // Transaction — income against A's account.
            _aTransactionId = Guid.NewGuid();
            db.Transactions.Add(new Transaction
            {
                Id          = _aTransactionId,
                Date        = new DateOnly(2025, 1, 1),
                Amount      = 100m,
                AccountId   = _aAccountId,
                CategoryId  = incomeCategory.Id,
                UserId      = _userA.Id,
                CreatedAt   = DateTime.UtcNow,
            });

            // Goal Budget.
            _aBudgetId = Guid.NewGuid();
            db.Budgets.Add(new Budget
            {
                Id           = _aBudgetId,
                Name         = "IDOR-A-Budget",
                TargetAmount = 1000m,
                CurrencyId   = 1,
                StartDate    = new DateOnly(2025, 1, 1),
                IsActive     = true,
                GoalType     = "Saving",
                UserId       = _userA.Id,
            });

            // CategoryBudget — expense category required.
            _aCategoryBudgetId = Guid.NewGuid();
            db.CategoryBudgets.Add(new CategoryBudget
            {
                Id          = _aCategoryBudgetId,
                CategoryId  = expenseCategory.Id,
                CurrencyId  = 1,
                LimitAmount = 500m,
                IsActive    = true,
                UserId      = _userA.Id,
            });

            // RecurringTransaction.
            _aRecurringTxId = Guid.NewGuid();
            db.RecurringTransactions.Add(new RecurringTransaction
            {
                Id               = _aRecurringTxId,
                Name             = "IDOR-A-Recurring",
                AccountId        = _aAccountId,
                CategoryId       = incomeCategory.Id,
                Frequency        = Frequency.Monthly,
                NextDueDate      = new DateOnly(2025, 2, 1),
                IsActive         = true,
                ReminderBehaviour = ReminderBehaviour.SnapToCalendarDay,
                UserId           = _userA.Id,
            });

            // ImportProfile.
            _aImportProfileId = Guid.NewGuid();
            db.ImportProfiles.Add(new ImportProfile
            {
                Id             = _aImportProfileId,
                Name           = "IDOR-A-ImportProfile",
                Format         = ImportFormat.Csv,
                ColumnMappings = "{}",
                CreatedAt      = DateTime.UtcNow,
                UserId         = _userA.Id,
            });

            await db.SaveChangesAsync();
        }

        // Log in as User B and capture the session cookie.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _bSessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, _userB.Email!);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            // Data must be deleted before the user row (FK constraints).
            await db.RecurringTransactions.IgnoreQueryFilters().Where(r => r.UserId == u.Id).ExecuteDeleteAsync();
            await db.CategoryBudgets.IgnoreQueryFilters().Where(cb => cb.UserId == u.Id).ExecuteDeleteAsync();
            await db.Budgets.IgnoreQueryFilters().Where(b => b.UserId == u.Id).ExecuteDeleteAsync();
            await db.Transactions.IgnoreQueryFilters().Where(t => t.UserId == u.Id).ExecuteDeleteAsync();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.UserId == u.Id).ExecuteDeleteAsync();
            await db.ImportProfiles.IgnoreQueryFilters().Where(p => p.UserId == u.Id).ExecuteDeleteAsync();
            await db.Categories.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a cookieless HttpClient. Per-request helpers (<see cref="GetAsB"/>,
    /// <see cref="DeleteAsB"/>, <see cref="PatchAsB"/>) attach User B's session cookie
    /// explicitly via the <c>Cookie</c> header. The factory does not retain auth state
    /// between requests — that's intentional so the suite can swap users mid-test.
    /// </summary>
    private HttpClient CreateClientAsB() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>
    /// Sends a GET request authenticated as User B.
    /// </summary>
    private Task<HttpResponseMessage> GetAsB(string url)
    {
        var client = CreateClientAsB();
        var req    = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={_bSessionCookie}");
        return client.SendAsync(req);
    }

    /// <summary>
    /// Sends a DELETE request authenticated as User B with a valid CSRF token bound to B.
    /// </summary>
    private Task<HttpResponseMessage> DeleteAsB(string url)
    {
        var client = CreateClientAsB();
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, _userB.Id);
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={_bSessionCookie}; " +
            $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return client.SendAsync(req);
    }

    /// <summary>
    /// Sends a PATCH request authenticated as User B with a valid CSRF token bound to B.
    /// </summary>
    private Task<HttpResponseMessage> PatchAsB<T>(string url, T body)
    {
        var client = CreateClientAsB();
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, _userB.Id);
        var req = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={_bSessionCookie}; " +
            $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return client.SendAsync(req);
    }

    // ---------------------------------------------------------------------------
    // By-ID read tests — User B must get 404 for every User A resource
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UserB_cannot_read_UserA_account_by_id()
    {
        var resp = await GetAsB($"/api/accounts/{_aAccountId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant account read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_account_ledger()
    {
        var resp = await GetAsB($"/api/accounts/{_aAccountId}/ledger");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant ledger read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_transaction_by_id()
    {
        var resp = await GetAsB($"/api/transactions/{_aTransactionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant transaction read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_movement_type_by_id()
    {
        // GET /api/movements/{id} returns the movement type (transaction / transfer / liability-payment).
        var resp = await GetAsB($"/api/movements/{_aTransactionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant movement-type lookup must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_goal_budget_by_id()
    {
        var resp = await GetAsB($"/api/goal-budgets/{_aBudgetId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant goal-budget read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_budget_discriminator()
    {
        // GET /api/budgets/{id} returns the discriminator (CategoryBudget vs GoalBudget).
        var resp = await GetAsB($"/api/budgets/{_aBudgetId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant budget discriminator must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_category_budget_by_id()
    {
        var resp = await GetAsB($"/api/category-budgets/{_aCategoryBudgetId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant category-budget read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_recurring_transaction_by_id()
    {
        var resp = await GetAsB($"/api/recurring-transactions/{_aRecurringTxId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant recurring-transaction read must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_read_UserA_import_profile_by_id()
    {
        var resp = await GetAsB($"/api/import-profiles/{_aImportProfileId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant import-profile read must return 404, not {0}", resp.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Mutation tests — User B must get 404 on write operations against A's data
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UserB_cannot_delete_UserA_transaction()
    {
        var resp = await DeleteAsB($"/api/transactions/{_aTransactionId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant transaction delete must return 404, not {0}", resp.StatusCode);
    }

    [Fact]
    public async Task UserB_cannot_patch_cleared_on_UserA_transaction()
    {
        // PATCH /api/movements/{id}/cleared — body requires Type to route to the right handler.
        var resp = await PatchAsB($"/api/movements/{_aTransactionId}/cleared",
            new { type = "transaction", cleared = true });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant cleared-patch must return 404, not {0}", resp.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // List exclusion tests — A's rows must not appear in B's list responses
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UserB_account_list_excludes_UserA_accounts()
    {
        var resp = await GetAsB("/api/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().NotBeNull();
        var accountIds = list!.Select(e => e.GetProperty("id").GetGuid()).ToList();
        accountIds.Should().NotContain(_aAccountId,
            "User B's account list must not contain User A's account");
    }

    [Fact]
    public async Task UserB_movements_list_excludes_UserA_transactions()
    {
        var resp = await GetAsB("/api/movements");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // MovementsApiController returns MovementsPageDto, which has an "items" array.
        var page = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = page.GetProperty("items");
        var found = false;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idProp) && idProp.GetGuid() == _aTransactionId)
            {
                found = true;
                break;
            }
        }
        found.Should().BeFalse("User B's movements list must not contain User A's transaction");
    }

    [Fact]
    public async Task UserB_goal_budgets_list_excludes_UserA_budgets()
    {
        var resp = await GetAsB("/api/goal-budgets");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().NotBeNull();
        var budgetIds = list!.Select(e => e.GetProperty("id").GetGuid()).ToList();
        budgetIds.Should().NotContain(_aBudgetId,
            "User B's goal-budget list must not contain User A's budget");
    }

    // ---------------------------------------------------------------------------
    // Negative-assertion test — global EF query filter alone catches the leak
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Seeds an Account for User A bypassing the query filter, then enters User B's
    /// scope without using any service-layer .Owned(user) chain, and issues a raw
    /// db.Accounts.ToListAsync(). Asserts the list does NOT contain A's account.
    ///
    /// This pins the global query filter as the safety net: it must stop the leak
    /// independently of service-layer discipline.
    /// </summary>
    [Fact]
    public async Task Global_query_filter_alone_catches_leak_without_service_Owned()
    {
        var leakAccountId = Guid.NewGuid();

        // Seed an account for User A directly, bypassing any app-level path.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.Accounts.Add(new Account
            {
                Id            = leakAccountId,
                Name          = "IDOR-GlobalFilter-LeakCheck",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
                UserId        = _userA.Id,
            });
            await seedDb.SaveChangesAsync();
        }

        // Enter User B's scope and issue a raw query — no .Owned(), no .Where(UserId == ...).
        // Ordering matters: AppDbContext must be resolved AFTER EnterAs so the scoped
        // ICurrentUserAccessor injected into the DbContext reads B's id from the AsyncLocal
        // already-set scope. Resolving the DbContext before EnterAs would capture an
        // accessor whose UserId resolves to Guid.Empty (the no-context safe default).
        using var queryScope = _factory.Services.CreateScope();
        var userScope = queryScope.ServiceProvider.GetRequiredService<IUserScope>();
        using (userScope.EnterAs(_userB.Id))
        {
            var db       = queryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accounts = await db.Accounts.ToListAsync();

            accounts.Should().NotContain(
                a => a.Id == leakAccountId,
                "the EF global query filter must hide User A's account when queried under User B's scope, " +
                "even with no service-layer .Owned() call");

            accounts.Should().OnlyContain(
                a => a.UserId == _userB.Id,
                "all accounts visible under User B's scope must belong to User B");
        }

        // Clean up the extra account so DisposeAsync doesn't have to know about it
        // (it's already under userA's Id and will be caught by DisposeAsync's user loop).
    }

    // ---------------------------------------------------------------------------
    // Intentionally skipped entities (documented)
    // ---------------------------------------------------------------------------
    //
    // Transfer — skipped. Testing IDOR on Transfers requires a second account for each
    // user (source + destination must share the same currency and both belong to the
    // same user). The additional setup complexity risks test flakiness and adds no
    // marginal value given that Transfers implement the same IUserOwned + global filter
    // + .Owned(user) pipeline as Transactions. A dedicated Transfer-specific IDOR test
    // should be added once the Transfer SPA page is cut over (Stage 7 Batch 2+).
    //
    // SavedReport — no SPA API surface yet. The SavedReport entity exists in the DB but
    // there is no /api/saved-reports controller at this stage. Add an IDOR test when the
    // SavedReport API is introduced.
}
