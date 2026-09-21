using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 13.8 Task 1 — ExportJob is a new IUserOwned entity with no RLS policy yet
/// (that lands in Task 2's migration, which does not exist yet). The real "ExportJobs"
/// table therefore does not exist in this DB either, so this test bootstraps it itself
/// via a throwaway single-entity DbContext (EF's own DDL, run as ceres_migrator — the
/// table owner — so default privileges hand ceres_app/ceres_admin access automatically,
/// same as a real migration would) and drops it again in teardown. This is intentionally
/// NOT `dotnet ef migrations add` / no project migration file is created.
///
/// This test is INTENTIONALLY RED: it proves the gap exists (user B currently sees user
/// A's row) so Task 2 has a red test to turn green, matching the Group 1 FromSqlRaw-bypass
/// style used for every other table.
/// </summary>
[Collection("RlsTests")]
public class ExportJobRlsTests
{
    private readonly RlsTestFixture _fixture;

    public ExportJobRlsTests(RlsTestFixture fixture) => _fixture = fixture;

    private sealed class ExportJobOnlyContext(DbContextOptions<ExportJobOnlyContext> options)
        : DbContext(options)
    {
        public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExportJob>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.TokenLookup).IsRequired();
            });
        }
    }

    [Fact]
    public async Task ExportJob_is_invisible_across_users_under_rls()
    {
        await EnsureExportJobsTableExistsAsync();

        var jobA = NewExportJob(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ExportJobs.Add(jobA);
            await admin.SaveChangesAsync();
        }

        try
        {
            // IgnoreQueryFilters strips EF's application-layer UserId filter (which
            // ConfigureGlobalQueryFilters now applies to ExportJob too, since it's
            // IUserOwned) so the ONLY thing that can enforce isolation here is the
            // Postgres RLS policy — same reasoning as RlsParityMetaTests. Without it,
            // this test would pass today for the wrong reason (the EF filter, not RLS).
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.ExportJobs
                .FromSqlRaw("SELECT * FROM \"ExportJobs\"")
                .IgnoreQueryFilters()
                .ToListAsync();

            rowsVisibleToB.Should().BeEmpty(
                "user B must not see user A's ExportJob once RLS is enforced (Task 2)");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ExportJobs
                .IgnoreQueryFilters()
                .Where(j => j.Id == jobA.Id)
                .ExecuteDeleteAsync();

            await DropExportJobsTableAsync();
        }
    }

    // ceres_migrator owns created tables, so default privileges (scripts/setup-postgres-roles.sql)
    // grant ceres_app/ceres_admin access automatically — no explicit GRANT needed here.
    // EnsureCreatedAsync no-ops on a DB that already has tables (it only bootstraps a
    // truly empty database), so the DDL is generated from this single-entity model and
    // executed directly instead.
    private static async Task EnsureExportJobsTableExistsAsync()
    {
        var options = new DbContextOptionsBuilder<ExportJobOnlyContext>()
            .UseNpgsql(RlsTestFixture.MigratorConnectionString)
            .Options;
        await using var ctx = new ExportJobOnlyContext(options);
        var script = ctx.Database.GenerateCreateScript();
        await ctx.Database.ExecuteSqlRawAsync(script);
    }

    private static async Task DropExportJobsTableAsync()
    {
        var options = new DbContextOptionsBuilder<ExportJobOnlyContext>()
            .UseNpgsql(RlsTestFixture.MigratorConnectionString)
            .Options;
        await using var ctx = new ExportJobOnlyContext(options);
        await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"ExportJobs\"");
    }

    private static ExportJob NewExportJob(Guid userId) => new()
    {
        Id          = Guid.NewGuid(),
        UserId      = userId,
        Status      = ExportJobStatus.Pending,
        Format      = ExportFormat.Zip,
        RequestedAt = DateTime.UtcNow,
        TokenLookup = Array.Empty<byte>(),
    };
}
