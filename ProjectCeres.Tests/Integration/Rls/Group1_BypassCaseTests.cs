using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 Group 1: FromSqlRaw bypass. Under user B's context, a raw
/// SQL query against a user-owned table returns ONLY user B's rows — proving RLS
/// catches the EF-query-filter-bypass case.
///
/// <para>
/// Covers one representative table per archetype (finance, auth-internal). The
/// parity test enforces that every other user-owned table also has a
/// <c>user_isolation</c> policy installed, so the bypass property holds uniformly.
/// </para>
/// </summary>
[Collection("RlsTests")]
public class Group1_BypassCaseTests
{
    private readonly RlsTestFixture _fixture;

    public Group1_BypassCaseTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FromSqlRaw_against_Accounts_under_userB_returns_only_userB_rows()
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
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.Accounts
                .FromSqlRaw("SELECT * FROM \"Accounts\"")
                .ToListAsync();

            rowsVisibleToB.Should().ContainSingle()
                .Which.UserId.Should().Be(_fixture.UserB);
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.Accounts
                .IgnoreQueryFilters()
                .Where(a => a.Id == accountA.Id || a.Id == accountB.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task FromSqlRaw_against_UserSessions_under_userB_returns_only_userB_rows()
    {
        var sessionA = NewSession(_fixture.UserA);
        var sessionB = NewSession(_fixture.UserB);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.UserSessions.AddRange(sessionA, sessionB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.UserSessions
                .FromSqlRaw("SELECT * FROM \"UserSessions\"")
                .ToListAsync();

            rowsVisibleToB.Should().ContainSingle()
                .Which.UserId.Should().Be(_fixture.UserB);
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.UserSessions
                .IgnoreQueryFilters()
                .Where(s => s.Id == sessionA.Id || s.Id == sessionB.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task FromSqlRaw_against_AuditLogs_under_userB_returns_only_userB_rows()
    {
        var entryA = NewAuditLog(_fixture.UserA);
        var entryB = NewAuditLog(_fixture.UserB);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.AuditLogs.AddRange(entryA, entryB);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.AuditLogs
                .FromSqlRaw("SELECT * FROM \"AuditLogs\"")
                .ToListAsync();

            rowsVisibleToB.Should().ContainSingle()
                .Which.UserId.Should().Be(_fixture.UserB);
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.Id == entryA.Id || a.Id == entryB.Id)
                .ExecuteDeleteAsync();
        }
    }

    // -------------- helpers --------------

    private static Account NewAccount(Guid userId) => new()
    {
        Id            = Guid.NewGuid(),
        UserId        = userId,
        Name          = $"RLS Test {Guid.NewGuid():N}",
        AccountTypeId = 1,
        CurrencyId    = 1,
        IsActive      = true,
    };

    private static UserSession NewSession(Guid userId) => new()
    {
        Id                  = Guid.NewGuid(),
        UserId              = userId,
        PersistentTokenHash = null,
        IpCreatedAt         = "127.0.0.1",
        UserAgent           = "rls-test",
        CreatedAt           = DateTime.UtcNow,
        LastUsedAt          = DateTime.UtcNow,
        IsPersistent        = false,
    };

    private static AuditLog NewAuditLog(Guid userId) => new()
    {
        Id         = Guid.NewGuid(),
        UserId     = userId,
        Action     = AuditLogAction.LoginSucceeded,
        OccurredAt = DateTime.UtcNow,
        IpAddress  = "127.0.0.1",
    };
}
