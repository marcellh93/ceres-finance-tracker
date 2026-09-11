using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WAF tests covering Stage 5 UI/UX checklist items.
///
/// — Stage 5.1: Account Edit for a Liability account renders the LiabilityRepaymentType
///   select and the repayment type description hint. InterestRate field is present in HTML
///   but hidden via JS when RepaymentType != Amortising (JS show/hide not testable here).
///   → Tests removed: the Accounts SPA replaces them. Coverage moves to AccountForm.test.tsx.
///
/// — Stage 5.2: Amortising account Ledger page renders the Payoff Projection section.
///   → Tests removed: the SPA replaces them. Coverage moves to AccountLedger.test.tsx.
///
/// — Stage 6.1/6.2: Recurring Transaction Razor views (Create, Upcoming).
///   → Tests removed: RecurringTransactions SPA replaces these routes; the Razor
///     controller now returns 302 redirects. Coverage moves to the React test suite.
///
/// Seed data IDs used:
///   AccountTypeId 1 = Asset, 2 = Liability
///   CurrencyId    1 = EUR
/// </summary>
[Collection("IntegrationParallel4")]
public class UiVerificationTests : IntegrationTestBase<Bucket4Factory>, IAsyncLifetime
{
    private readonly Bucket4Factory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededAccountIds = [];

    public UiVerificationTests(Bucket4Factory factory, Bucket4Database bucketDb) : base(factory, bucketDb)
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

        if (_seededAccountIds.Count > 0)
            await db.Accounts
                .Where(a => _seededAccountIds.Contains(a.Id))
                .ExecuteDeleteAsync();
    }

    // -------------------------------------------------------------------------
    // All Razor-layer UI verification tests have been removed as the SPA replaces
    // the Razor views. See the class summary for migration notes.
    // -------------------------------------------------------------------------
}
