using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.AppRole;
using ProjectCeres.Tests.Integration.Authentication;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Integration.Retention;

[Collection("AppRoleTests")]
public class RetentionPurgeTests : AppRoleTestBase
{
    private readonly List<Guid> _userIds = new();
    private readonly List<string> _failedLoginMarkers = new();

    public RetentionPurgeTests(AppRoleFixture fixture) : base(fixture) { }

    private async Task<Guid> RegisterUserAsync(string marker)
    {
        var email = $"{marker}@approle-test.local";
        var client = Factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        await using var admin = Factory.NewAdminContext();
        var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
        _userIds.Add(u.Id);
        return u.Id;
    }

    [Fact]
    public async Task PurgeAuditLogAsync_deletes_only_rows_past_12_months()
    {
        var marker = $"rp-audit-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.AuditLogs.AddRange(
                // CK_AuditLog_EntityPair (DB check constraint): EntityType/EntityId must both be
                // null or both be set — EntityId is required here because EntityType is used as the marker.
                new AuditLog { Id = Guid.NewGuid(), UserId = userId, Action = AuditLogAction.LoginSucceeded, EntityType = marker, EntityId = Guid.NewGuid(), OccurredAt = now.AddDays(-366), IpAddress = "127.0.0.1" }, // past 12mo → deleted
                new AuditLog { Id = Guid.NewGuid(), UserId = userId, Action = AuditLogAction.LoginSucceeded, EntityType = marker, EntityId = Guid.NewGuid(), OccurredAt = now.AddDays(-364), IpAddress = "127.0.0.1" }  // inside → kept
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeAuditLogAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EntityType == marker).ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].OccurredAt.Should().BeAfter(now.AddDays(-365));
    }

    [Fact]
    public async Task PurgeFailedLoginsAsync_deletes_only_rows_past_1_year()
    {
        var marker = $"rp-fail-{Guid.NewGuid():N}";
        _failedLoginMarkers.Add(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.FailedLoginAttempts.AddRange(
                new FailedLoginAttempt { Id = Guid.NewGuid(), EmailAttempted = $"{marker}-old", IpAddress = "127.0.0.1", UserAgent = "test", Reason = FailedLoginReason.BadCredentials, OccurredAt = now.AddDays(-366) },
                new FailedLoginAttempt { Id = Guid.NewGuid(), EmailAttempted = $"{marker}-new", IpAddress = "127.0.0.1", UserAgent = "test", Reason = FailedLoginReason.BadCredentials, OccurredAt = now.AddDays(-364) }
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeFailedLoginsAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.FailedLoginAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.EmailAttempted!.StartsWith(marker)).Select(f => f.EmailAttempted).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-new" });
    }

    [Fact]
    public async Task PurgeSavedReportsAsync_deletes_only_soft_deleted_past_90_days()
    {
        var marker = $"rp-rep-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.SavedReports.AddRange(
                NewReport(userId, $"{marker}-old", deletedAt: now.AddDays(-91)),    // soft-deleted past 90d → deleted
                NewReport(userId, $"{marker}-recent", deletedAt: now.AddDays(-89)), // soft-deleted inside → kept
                NewReport(userId, $"{marker}-live", deletedAt: null)                // never deleted → kept regardless
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeSavedReportsAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.SavedReports.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Name.StartsWith(marker)).Select(r => r.Name).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-recent", $"{marker}-live" });
    }

    [Fact]
    public async Task PurgeImportProfilesAsync_deletes_only_soft_deleted_past_90_days()
    {
        var marker = $"rp-prof-{Guid.NewGuid():N}";
        var userId = await RegisterUserAsync(marker);
        var now = DateTime.UtcNow;
        await using (var admin = Factory.NewAdminContext())
        {
            admin.Context.ImportProfiles.AddRange(
                NewProfile(userId, $"{marker}-old", deletedAt: now.AddDays(-91)),
                NewProfile(userId, $"{marker}-recent", deletedAt: now.AddDays(-89)),
                NewProfile(userId, $"{marker}-live", deletedAt: null)
            );
            await admin.Context.SaveChangesAsync();
        }
        var clock = Factory.Services.GetRequiredService<TimeProvider>();
        await using (var run = Factory.NewAdminContext())
            await RetentionPurge.PurgeImportProfilesAsync(run.Context, clock, default);

        await using var verify = Factory.NewAdminContext();
        var remaining = await verify.Context.ImportProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Name.StartsWith(marker)).Select(p => p.Name).ToListAsync();
        remaining.Should().BeEquivalentTo(new[] { $"{marker}-recent", $"{marker}-live" });
    }

    // ReportTypeId = 1 ("Net Worth Statement") is seeded reference data (AppDbContext.HasData);
    // SavedReport.ReportType is a required non-null FK navigation.
    private static SavedReport NewReport(Guid userId, string name, DateTime? deletedAt) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Name = name, ReportTypeId = 1,
        CreatedAt = DateTime.UtcNow, DeletedAt = deletedAt,
    };

    private static ImportProfile NewProfile(Guid userId, string name, DateTime? deletedAt) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Name = name, CreatedAt = DateTime.UtcNow, DeletedAt = deletedAt,
    };

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        foreach (var userId in _userIds)
        {
            await admin.Context.SavedReports.IgnoreQueryFilters().Where(r => r.UserId == userId).ExecuteDeleteAsync();
            await admin.Context.ImportProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync();
            await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteDeleteAsync();
        }
        foreach (var marker in _failedLoginMarkers)
        {
            await admin.Context.FailedLoginAttempts.IgnoreQueryFilters()
                .Where(f => f.EmailAttempted!.StartsWith(marker)).ExecuteDeleteAsync();
        }
    }
}
