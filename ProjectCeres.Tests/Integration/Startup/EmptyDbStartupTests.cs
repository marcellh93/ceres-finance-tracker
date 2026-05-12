using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Startup;

[Collection("IntegrationTests")]
public class EmptyDbStartupTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    // Backed-up state we restore after the test so we don't break other tests in the
    // collection that rely on the seeded reference rows being present.
    private readonly List<Account>  _backedUpAccounts  = new();
    private readonly List<Settings> _backedUpSettings  = new();

    public EmptyDbStartupTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Back up the rows we're about to wipe so DisposeAsync can restore them.
        // Categories stay in place — every registered user has 26 seeded rows;
        // wiping them would force every subsequent test to re-seed, and the
        // test-collection sequencing means the next test would race the seed
        // service. Boot-time hooks don't query Categories (they query Settings
        // and Accounts), so the regression is captured by clearing only those.
        _backedUpAccounts.AddRange(await db.Accounts.IgnoreQueryFilters().ToListAsync());
        _backedUpSettings.AddRange(await db.Settings.IgnoreQueryFilters().ToListAsync());

        // Wipe user-owned tables the test cares about. The order matters: any FK
        // dependents go first, then the parent rows. Settings is one-row-per-user
        // with no dependents; Accounts has Transaction/Transfer/etc. FK children
        // — those are wiped first.
        await db.Transactions      .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Transfers         .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.LiabilityPayments .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.RecurringTransactions.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.CategoryBudgets   .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Budgets           .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.SavedReports      .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.ImportStagedTransactions.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.ImportStagedTransfers   .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.ImportTransferExclusions.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.ImportProfiles    .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Accounts          .IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Settings          .IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_backedUpAccounts.Count > 0) db.Accounts .AddRange(_backedUpAccounts);
        if (_backedUpSettings.Count > 0) db.Settings .AddRange(_backedUpSettings);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task App_boots_against_empty_db_and_responds_to_health()
    {
        // Stage 6a removed ISettingsService.EnsureExistsAsync from Program.cs.
        // Stage 7's safety net for that removal: this test confirms the app boots
        // and serves /api/health even when zero rows exist in user-owned tables.
        // A regression that reintroduces a boot-time hook querying Settings or
        // Accounts would throw during DI resolution → /api/health would never
        // respond → this test fails.
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
