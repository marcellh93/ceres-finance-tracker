using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 13.8 Task 2 — ExportJobs is now a real migrated table with a user_isolation
/// policy (AddExportJobs migration), so this follows the same Group 1 FromSqlRaw-bypass
/// pattern as Accounts/UserSessions/AuditLogs: no throwaway self-bootstrapped table.
/// </summary>
[Collection("RlsTests")]
public class ExportJobRlsTests
{
    private readonly RlsTestFixture _fixture;

    public ExportJobRlsTests(RlsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExportJob_is_invisible_across_users_under_rls()
    {
        var jobA = NewExportJob(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ExportJobs.Add(jobA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appB = _fixture.CreateAppContext(_fixture.UserB);
            var rowsVisibleToB = await appB.ExportJobs
                .FromSqlRaw("SELECT * FROM \"ExportJobs\"")
                .ToListAsync();

            rowsVisibleToB.Should().BeEmpty(
                "user B must not see user A's ExportJob under RLS");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ExportJobs
                .IgnoreQueryFilters()
                .Where(j => j.Id == jobA.Id)
                .ExecuteDeleteAsync();
        }
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
