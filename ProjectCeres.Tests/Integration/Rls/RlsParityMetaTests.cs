using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 9.5b ship-gate. Two tests the old parity oracle could not give us:
/// (1) the meta-test proves <see cref="RlsParityStartupCheck"/> actually THROWS when an
/// applied user-owned table loses forced RLS — the present-but-unprotected input the old
/// hand-list oracle was blind to (Hole A); (2) the isolation test proves a ceres_app
/// context booted through the production DI graph cannot see another user's row.
///
/// D1 both-flags live strictness is NOT re-tested here — <see cref="ParityTests"/>
/// (Every_user_owned_table_has_FORCE_RLS_enabled) already asserts relrowsecurity AND
/// relforcerowsecurity across UserOwnedModel.RlsTables. Duplicating it would add noise.
/// </summary>
[Collection("RlsTests")]
public class RlsParityMetaTests
{
    private readonly RlsTestFixture _fixture;

    public RlsParityMetaTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task StartupCheck_throws_when_an_applied_user_owned_table_lacks_forced_rls()
    {
        // Turn FORCE RLS off on Accounts, assert the check refuses to start naming Accounts,
        // then restore in finally so the shared test DB is left clean. ALTER TABLE requires
        // table ownership, so the toggle runs as ceres_migrator (the table owner) — ceres_admin
        // has BYPASSRLS but is not the owner. The RlsTests collection serializes its tests, so
        // this transient mutation cannot race a sibling test.
        await using var admin = _fixture.CreateAdminContext();
        await SetForceRlsAsync("Accounts", false);
        try
        {
            var act = async () => await RlsParityStartupCheck
                .EnsureAppliedUserOwnedTablesAreRlsProtectedAsync(admin.Model, TestDbFixture.AppConnectionString);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*refusing to start*Accounts*");
        }
        finally
        {
            await SetForceRlsAsync("Accounts", true);
        }
    }

    // Toggle FORCE ROW LEVEL SECURITY as ceres_migrator (the table owner). ENABLE/FORCE RLS
    // is DDL and Postgres requires ownership — ceres_admin's BYPASSRLS is not enough.
    private static async Task SetForceRlsAsync(string table, bool force)
    {
        await using var conn = new NpgsqlConnection(TestDbFixture.MigratorConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{table}\" {(force ? "FORCE" : "NO FORCE")} ROW LEVEL SECURITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task StartupCheck_passes_when_every_applied_user_owned_table_has_forced_rls()
    {
        // Positive control: with the DB in its correct state, the check does NOT throw.
        // Without this, the meta-test above can't distinguish "threw for the right reason"
        // from "throws on any DB state".
        await using var admin = _fixture.CreateAdminContext();

        var act = async () => await RlsParityStartupCheck
            .EnsureAppliedUserOwnedTablesAreRlsProtectedAsync(admin.Model, TestDbFixture.AppConnectionString);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task App_context_cannot_see_a_row_another_user_owns()
    {
        // Booted-host isolation: seed userA's Account via admin (BYPASSRLS), then prove a
        // ceres_app context acting as userB sees zero of A's rows (negative), while a context
        // acting as userA sees exactly that row (positive control). Without the positive
        // control, "Be(0)" could mean "RLS works" OR "the app sees nothing at all".
        using var factory = new DualContextWebApplicationFactory();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var accountA = new Account
        {
            Id = Guid.NewGuid(),
            UserId = userA,
            Name = $"meta RLS {Guid.NewGuid():N}",
            AccountTypeId = 1,
            CurrencyId = 1,
            IsActive = true,
        };

        await using (var admin = factory.NewAdminContext())
        {
            admin.Context.Accounts.Add(accountA);
            await admin.Context.SaveChangesAsync();
        }

        try
        {
            await using (var appB = factory.NewAppContext(actingAs: userB))
            {
                (await appB.Context.Accounts.CountAsync(a => a.UserId == userA))
                    .Should().Be(0, "ceres_app acting as userB must not see userA's row");
            }

            await using (var appA = factory.NewAppContext(actingAs: userA))
            {
                (await appA.Context.Accounts.CountAsync(a => a.UserId == userA))
                    .Should().Be(1, "ceres_app acting as userA must see its own row (positive control)");
            }
        }
        finally
        {
            await using var admin = factory.NewAdminContext();
            await admin.Context.Accounts.IgnoreQueryFilters()
                .Where(a => a.Id == accountA.Id).ExecuteDeleteAsync();
        }
    }
}
