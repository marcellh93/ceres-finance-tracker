using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 7.5 / ADR-0068 Group 2: write rejection. Under user A's context (runtime
/// <c>ceres_app</c> role with the GUC set to A), the policy <c>WITH CHECK</c>
/// rejects INSERT/UPDATE of rows stamped to a foreign UserId with SqlState 42501.
/// DELETE against a foreign UserId is filtered by <c>USING</c> and affects 0 rows.
/// </summary>
[Collection("RlsTests")]
public class Group2_WriteRejectionTests
{
    private readonly RlsTestFixture _fixture;

    public Group2_WriteRejectionTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task INSERT_Account_with_foreign_UserId_raises_RlsPolicyViolation()
    {
        await using var appA = _fixture.CreateAppContext(_fixture.UserA);
        appA.Accounts.Add(new Account
        {
            Id            = Guid.NewGuid(),
            UserId        = _fixture.UserB,                // foreign
            Name          = $"Foreign attempt {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId    = 1,
            IsActive      = true,
        });

        var act = async () => await appA.SaveChangesAsync();

        // Stage 7.6.2: the RlsExceptionTranslator wraps PostgresException 42501 on a
        // user-owned table into RlsPolicyViolationException carrying the diagnostic data.
        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("Accounts");
        ex.Which.GucUserId.Should().Be(_fixture.UserA);
        ex.Which.OriginalException.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task UPDATE_account_to_set_foreign_UserId_raises_RlsPolicyViolation()
    {
        // Seed an account for user A via the admin role (avoids interceptor-tracking quirks).
        var accountId = Guid.NewGuid();
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.Add(new Account
            {
                Id            = accountId,
                UserId        = _fixture.UserA,
                Name          = $"RLS Update Target {Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
            });
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var account = await appA.Accounts.SingleAsync(a => a.Id == accountId);
            account.UserId = _fixture.UserB;

            var act = async () => await appA.SaveChangesAsync();

            var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
            ex.Which.TableName.Should().Be("Accounts");
            ex.Which.GucUserId.Should().Be(_fixture.UserA);
            ex.Which.OriginalException.SqlState.Should().Be("42501");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.Accounts.IgnoreQueryFilters()
                .Where(a => a.Id == accountId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task DELETE_against_foreign_UserId_row_affects_0_rows()
    {
        // Seed a row for user B via the admin role.
        var accountId = Guid.NewGuid();
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Accounts.Add(new Account
            {
                Id            = accountId,
                UserId        = _fixture.UserB,
                Name          = $"RLS Delete Target {Guid.NewGuid():N}",
                AccountTypeId = 1,
                CurrencyId    = 1,
                IsActive      = true,
            });
            await admin.SaveChangesAsync();
        }

        try
        {
            // Acting as user A — the policy USING clause filters B's row out before DELETE.
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var affected = await appA.Accounts
                .Where(a => a.Id == accountId)
                .ExecuteDeleteAsync();

            affected.Should().Be(0, "RLS USING clause hides B's row from A; DELETE matches nothing");

            // Verify the row STILL exists via admin context.
            await using var verify = _fixture.CreateAdminContext();
            var stillThere = await verify.Accounts
                .IgnoreQueryFilters()
                .AnyAsync(a => a.Id == accountId);
            stillThere.Should().BeTrue("the row was not actually deleted because RLS filtered it from A's view");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.Accounts.IgnoreQueryFilters()
                .Where(a => a.Id == accountId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task INSERT_UserSession_with_foreign_UserId_raises_RlsPolicyViolation()
    {
        await using var appA = _fixture.CreateAppContext(_fixture.UserA);
        appA.UserSessions.Add(new UserSession
        {
            Id                  = Guid.NewGuid(),
            UserId              = _fixture.UserB,            // foreign
            IpCreatedAt         = "127.0.0.1",
            UserAgent           = "rls-test",
            CreatedAt           = DateTime.UtcNow,
            LastUsedAt          = DateTime.UtcNow,
            IsPersistent        = false,
        });

        var act = async () => await appA.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("UserSessions");
        ex.Which.GucUserId.Should().Be(_fixture.UserA);
    }
}
