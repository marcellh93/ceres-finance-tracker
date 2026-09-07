using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 Group 3: admin + background paths. The
/// <c>AdminDbContext</c> (Postgres role <c>ceres_admin</c>, <c>BYPASSRLS</c>) reads
/// across all users when combined with <c>IgnoreQueryFilters()</c>. The
/// <c>AppDbContext</c> (Postgres role <c>ceres_app</c>, NOBYPASSRLS) under a
/// specific user sees only that user's rows even with <c>IgnoreQueryFilters()</c> —
/// RLS is the wall the EF filter can't lift.
///
/// <para>
/// The background-job scope route (<c>BackgroundJobScope.RunAsync</c>) lands in
/// Commit 6 and has its own integration tests in that commit.
/// </para>
/// </summary>
[Collection("RlsTests")]
public class Group3_AdminAndBackgroundTests
{
    private readonly RlsTestFixture _fixture;

    public Group3_AdminAndBackgroundTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AdminDbContext_with_IgnoreQueryFilters_returns_rows_from_all_users()
    {
        var accountA = NewAccount(_fixture.UserA);
        var accountB = NewAccount(_fixture.UserB);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.AddRange(accountA, accountB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var admin = _fixture.CreateAdminContext();
            var rows = await admin.Accounts
                .IgnoreQueryFilters()
                .Where(a => a.Id == accountA.Id || a.Id == accountB.Id)
                .ToListAsync();

            rows.Should().HaveCount(2)
                .And.Contain(a => a.UserId == _fixture.UserA)
                .And.Contain(a => a.UserId == _fixture.UserB);
        }
        finally
        {
            await Cleanup(accountA.Id, accountB.Id);
        }
    }

    [Fact]
    public async Task AppDbContext_under_userA_does_NOT_see_userB_rows_even_with_IgnoreQueryFilters()
    {
        // IgnoreQueryFilters() bypasses the EF global query filter. RLS still applies
        // because it's enforced one layer below EF.
        var accountA = NewAccount(_fixture.UserA);
        var accountB = NewAccount(_fixture.UserB);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.AddRange(accountA, accountB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var rows = await appA.Accounts
                .IgnoreQueryFilters()
                .Where(a => a.Id == accountA.Id || a.Id == accountB.Id)
                .ToListAsync();

            rows.Should().ContainSingle()
                .Which.UserId.Should().Be(_fixture.UserA,
                    "RLS filters B's row out of A's view regardless of IgnoreQueryFilters()");
        }
        finally
        {
            await Cleanup(accountA.Id, accountB.Id);
        }
    }

    // Stage 12.5.2: pin the SupportTicket list read path specifically. The admin ticket-list
    // endpoint reads adminDb.SupportTickets.IgnoreQueryFilters() across all users; these two
    // prove that read relies on ceres_admin BYPASSRLS — the admin sees every owner's ticket,
    // and a ceres_app context (a non-owner) sees zero even with the EF filter stripped. Without
    // this, the cross-tenant list could regress to leaking-or-hiding and only the generic Account
    // case above would notice.
    [Fact]
    public async Task Admin_list_read_path_sees_SupportTickets_from_all_users()
    {
        var ticketA = NewTicket(_fixture.UserA);
        var ticketB = NewTicket(_fixture.UserB);
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.SupportTickets.AddRange(ticketA, ticketB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var admin = _fixture.CreateAdminContext();
            var rows = await admin.SupportTickets
                .IgnoreQueryFilters()
                .Where(t => t.Id == ticketA.Id || t.Id == ticketB.Id)
                .ToListAsync();
            rows.Should().HaveCount(2)
                .And.Contain(t => t.UserId == _fixture.UserA)
                .And.Contain(t => t.UserId == _fixture.UserB);
        }
        finally
        {
            await CleanupTickets(ticketA.Id, ticketB.Id);
        }
    }

    [Fact]
    public async Task App_context_does_NOT_see_another_users_SupportTicket_even_with_IgnoreQueryFilters()
    {
        var ticketA = NewTicket(_fixture.UserA);
        var ticketB = NewTicket(_fixture.UserB);
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.SupportTickets.AddRange(ticketA, ticketB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var rows = await appA.SupportTickets
                .IgnoreQueryFilters()
                .Where(t => t.Id == ticketA.Id || t.Id == ticketB.Id)
                .ToListAsync();
            rows.Should().ContainSingle()
                .Which.UserId.Should().Be(_fixture.UserA,
                    "RLS filters B's ticket out of A's view regardless of IgnoreQueryFilters()");
        }
        finally
        {
            await CleanupTickets(ticketA.Id, ticketB.Id);
        }
    }

    private static SupportTicket NewTicket(Guid userId) => new()
    {
        Id        = Guid.NewGuid(),
        UserId    = userId,
        Subject   = $"Group3 RLS ticket {Guid.NewGuid():N}",
        Status    = SupportTicketStatus.Open,
        Priority  = SupportTicketPriority.Normal,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private async Task CleanupTickets(Guid a, Guid b)
    {
        await using var admin = _fixture.CreateAdminContext();
        await admin.SupportTickets.IgnoreQueryFilters()
            .Where(x => x.Id == a || x.Id == b).ExecuteDeleteAsync();
    }

    private static Account NewAccount(Guid userId) => new()
    {
        Id            = Guid.NewGuid(),
        UserId        = userId,
        Name          = $"Group3 RLS {Guid.NewGuid():N}",
        AccountTypeId = 1,
        CurrencyId    = 1,
        IsActive      = true,
    };

    private async Task Cleanup(Guid a, Guid b)
    {
        await using var admin = _fixture.CreateAdminContext();
        await admin.Accounts
            .IgnoreQueryFilters()
            .Where(x => x.Id == a || x.Id == b)
            .ExecuteDeleteAsync();
    }
}
