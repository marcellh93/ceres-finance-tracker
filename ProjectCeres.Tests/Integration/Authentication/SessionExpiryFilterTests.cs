using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;
using ProjectCeres.Tests.Integration.AppRole;
using Xunit;

namespace ProjectCeres.Tests.Integration.Authentication;

// Stage 12.10 Task A: GetActiveAsync must hide sessions that are dead by expiry, not
// just revoked ones. Derives from AppRoleTestBase (not a bare [Collection("Waf")]) —
// the seam this suite actually exposes: Factory.NewAdminContext() for BYPASSRLS seed
// + read-back, FakeCurrentUserAccessor to bind SessionService to the seeded user.
[Collection("AppRoleTests")]
public class SessionExpiryFilterTests : AppRoleTestBase
{
    private Guid _userId;

    public SessionExpiryFilterTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetActiveAsync_hides_expired_ephemeral_and_stale_persistent_sessions()
    {
        // IpCreatedAt is varchar(45) (sized for an IP address) — the longest tag below
        // ("stale-persist.") leaves 31 chars for the marker, so this uses a short token
        // rather than the AppRoleTestBase.Marker (40 chars) which would overflow it.
        var marker = $"exp{Guid.NewGuid():N}"[..12];
        var email = $"{marker}-{Marker}@approle-test.local";
        var client = Factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });

        await using (var admin = Factory.NewAdminContext())
        {
            var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            _userId = u.Id;
        }
        var now = DateTime.UtcNow;

        UserSession Row(string tag, bool persistent, DateTime lastUsed, DateTime? revoked = null) => new()
        {
            Id = Guid.NewGuid(), UserId = _userId, IpCreatedAt = $"{tag}.{marker}",
            UserAgent = "test", IsPersistent = persistent,
            CreatedAt = now.AddDays(-40), LastUsedAt = lastUsed, RevokedAt = revoked,
        };

        // Seed via the BYPASSRLS admin context (the app-role context would 42501 on
        // these inserts because no user scope is active in a bare DI scope).
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.UserSessions.AddRange(
                Row("live-eph", persistent: false, lastUsed: now.AddMinutes(-5)),        // shown
                Row("dead-eph", persistent: false, lastUsed: now.AddMinutes(-31)),       // hidden (expired)
                Row("live-persist", persistent: true, lastUsed: now.AddDays(-3)),        // shown
                Row("stale-persist", persistent: true, lastUsed: now.AddDays(-31)),      // hidden (past 30d)
                Row("revoked", persistent: false, lastUsed: now.AddMinutes(-1), revoked: now)); // hidden
            await admin.Context.SaveChangesAsync();
        }

        // A bare Factory.Services scope's AppDbContext has no RLS GUC bound (FakeCurrentUserAccessor
        // only satisfies ICurrentUserAccessor at the C# level, not the Postgres session var), so a
        // read through it returns zero rows under ceres_app. NewAppContext(actingAs) is the seam
        // that actually enters the user's IUserScope before the connection opens (see
        // DualContextWebApplicationFactory doc comment) — pair it with FakeCurrentUserAccessor so
        // SessionService's own ICurrentUserAccessor dependency resolves to the same user.
        await using var app = Factory.NewAppContext(_userId);
        using var scope = Factory.Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var sessions = new SessionService(app.Context, new FakeCurrentUserAccessor(_userId), clock);
        var result = await sessions.GetActiveAsync(Guid.NewGuid());

        var shownTags = result.Select(r => r.IpCreatedAt).Where(ip => ip.EndsWith(marker)).ToList();
        shownTags.Should().BeEquivalentTo(new[] { $"live-eph.{marker}", $"live-persist.{marker}" });
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
