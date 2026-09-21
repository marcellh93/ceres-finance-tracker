using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Infrastructure;

/// <summary>
/// Stage 7.6.6 — single-responsibility helper that runs the EF migration set against
/// <c>project_ceres_test</c> as the <c>ceres_migrator</c> role (DDL + BYPASSRLS).
/// Idempotent: invoking <see cref="EnsureMigratedAsync"/> on an already-migrated
/// schema is a no-op. Extracted from the prior omnibus <c>TestDbFixture</c>.
/// </summary>
internal static class MigrationFixture
{
    private static readonly Guid SentinelUserId = TestDbFixture.SentinelUserId;

    public static async Task EnsureMigratedAsync()
    {
        var accessor = new FakeCurrentUserAccessor(SentinelUserId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.MigratorConnectionString)
            .Options;
        await using var migrator = new AppDbContext(options, accessor);
        await migrator.Database.MigrateAsync();
    }
}
