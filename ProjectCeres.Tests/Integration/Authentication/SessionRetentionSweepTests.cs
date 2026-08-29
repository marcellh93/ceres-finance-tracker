using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.AppRole;
using ProjectCeres.Tools;
using Xunit;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("AppRoleTests")]
public class SessionRetentionSweepTests : AppRoleTestBase
{
    private Guid _userId;

    public SessionRetentionSweepTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DeleteExpiredAsync_removes_only_rows_past_the_90_day_horizon()
    {
        var fullMarker = $"sweep-{Guid.NewGuid():N}";
        var email = $"{fullMarker}@approle-test.local";
        // IpCreatedAt is HasMaxLength(45); the longest tag below ("old-dead-eph.") plus
        // this marker must fit, so use a short 8-char slice instead of the full GUID hex.
        var marker = $"sw-{Guid.NewGuid():N}"[..11];
        var client = Factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        await using (var admin = Factory.NewAdminContext())
        {
            var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            _userId = u.Id;
        }
        var userId = _userId;
        var now = DateTime.UtcNow;

        UserSession Row(string tag, bool persistent, DateTime lastUsed, DateTime? revoked) => new()
        {
            Id = Guid.NewGuid(), UserId = userId, IpCreatedAt = $"{tag}.{marker}",
            UserAgent = "test", IsPersistent = persistent,
            CreatedAt = now.AddDays(-200), LastUsedAt = lastUsed, RevokedAt = revoked,
        };

        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.UserSessions.AddRange(
                Row("old-revoked", false, now.AddDays(-200), revoked: now.AddDays(-91)),  // deleted
                Row("new-revoked", false, now.AddDays(-200), revoked: now.AddDays(-89)),  // kept
                Row("old-dead-eph", false, now.AddDays(-91), revoked: null),              // deleted
                Row("old-dead-persist", true, now.AddDays(-91), revoked: null),           // deleted
                Row("live-persist", true, now.AddDays(-2), revoked: null));               // kept
            await admin.Context.SaveChangesAsync();
        }

        await using var verify = Factory.NewAdminContext();
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await SweepSessions.DeleteExpiredAsync(verify.Context, clock, default);

        var remaining = await verify.Context.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IpCreatedAt.EndsWith(marker)).Select(x => x.IpCreatedAt).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"new-revoked.{marker}", $"live-persist.{marker}" });
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
