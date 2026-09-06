using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 12.8.2 meta-proof (roadmap item 1573). AppRoleTestBase.AssertRlsVisibility now strips
/// the EF query filter so the ONLY isolation mechanism left is the Postgres RLS policy. This
/// test proves that stripping is load-bearing: with the policy disabled, the exact same
/// filter-stripped "other user sees zero" assertion FLIPS to seeing the owner's row.
///
/// Before Stage 12.8.2 the assertion did NOT strip the filter, so it passed whether RLS was on
/// or off — it proved the EF filter worked, not the database. One meta-proof (rather than
/// toggling the policy inside each of the nine call sites) is enough: all nine share the helper,
/// so proving the helper observes RLS proves it for every caller.
/// </summary>
[Collection("AppRoleTests")]
public sealed class RlsOffMakesOtherUserSeeOwnerRowTests : AppRoleTestBase
{
    private Guid _owner;
    private Guid _sessionId;

    public RlsOffMakesOtherUserSeeOwnerRowTests(AppRoleFixture fixture) : base(fixture) { }

    public override async Task DisposeAsync()
    {
        if (_sessionId == Guid.Empty) return;
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters()
            .Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Filter_stripped_negative_assertion_flips_when_the_RLS_policy_is_disabled()
    {
        // Seed one UserSession for the owner via the BYPASSRLS admin context (seed-via-admin,
        // act-via-app discipline). UserSession is IUserOwned and in UserOwnedModel.RlsTables.
        _owner = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.UserSessions.Add(new UserSession
            {
                Id = _sessionId,
                UserId = _owner,
                IpCreatedAt = "127.0.0.1",
                UserAgent = Marker,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsPersistent = false,
            });
            await admin.Context.SaveChangesAsync();
        }

        var otherUser = Guid.NewGuid();

        // Positive control: with RLS ENABLED, the filter-stripped other-user query sees zero —
        // and the owner sees exactly its row. This is the state AssertRlsVisibility asserts.
        await AssertRlsVisibilityCore<UserSession>(
            owner: _owner, otherUser: otherUser,
            predicate: s => s.Id == _sessionId, expectedOwnerCount: 1, Factory);

        // Now disable the policy on UserSessions and re-run ONLY the negative read. If the
        // filter-stripping is load-bearing, the other user now sees the owner's row (1), proving
        // the pre-12.8.2 assertion was passing on the EF filter, not on RLS. Restore in finally.
        await SetRlsEnabledAsync("UserSessions", false);
        try
        {
            await using var appOther = Factory.NewAppContext(otherUser);
            (await appOther.Context.UserSessions.IgnoreQueryFilters().CountAsync(s => s.Id == _sessionId))
                .Should().Be(1,
                    "with the EF filter stripped AND the RLS policy disabled, nothing enforces isolation — "
                    + "the other user now sees the owner's row. This is exactly what AssertRlsVisibility "
                    + "would have missed before it stripped the filter.");
        }
        finally
        {
            await SetRlsEnabledAsync("UserSessions", true);
        }
    }

    // Toggle ROW LEVEL SECURITY as ceres_migrator (the table owner) — DDL requires ownership,
    // and ceres_app is NOBYPASSRLS but not the owner. DISABLE (not just NO FORCE) is what stops
    // enforcement for a non-owner role. The AppRoleTests collection is serialized, so this
    // transient mutation cannot race a sibling test.
    private static async Task SetRlsEnabledAsync(string table, bool enabled)
    {
        await using var conn = new NpgsqlConnection(TestDbFixture.MigratorConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = enabled
            ? $"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY; ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;"
            : $"ALTER TABLE \"{table}\" NO FORCE ROW LEVEL SECURITY; ALTER TABLE \"{table}\" DISABLE ROW LEVEL SECURITY;";
        await cmd.ExecuteNonQueryAsync();
    }
}
