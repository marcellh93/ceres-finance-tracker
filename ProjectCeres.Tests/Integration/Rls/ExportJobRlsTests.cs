using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Rls;

/// <summary>
/// Stage 13.8 Task 2 — ExportJobs is now a real migrated table with a user_isolation
/// policy (AddExportJobs migration), so this follows the same Group 1 FromSqlRaw-bypass
/// pattern as Accounts/UserSessions/AuditLogs: no throwaway self-bootstrapped table.
/// Also covers the USING positive control and the WITH CHECK negative control
/// (Group 2 INSERT-foreign-UserId pattern), so both halves of the policy are pinned.
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

    [Fact]
    public async Task ExportJob_is_visible_to_its_own_owner_under_rls()
    {
        // Positive control: the policy must not simply hide every row — user A
        // scoped to their own GUC must still see their own ExportJob.
        var jobA = NewExportJob(_fixture.UserA);

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ExportJobs.Add(jobA);
            await admin.SaveChangesAsync();
        }

        try
        {
            await using var appA = _fixture.CreateAppContext(_fixture.UserA);
            var rowsVisibleToA = await appA.ExportJobs
                .FromSqlRaw("SELECT * FROM \"ExportJobs\"")
                .ToListAsync();

            rowsVisibleToA.Should().ContainSingle()
                .Which.Id.Should().Be(jobA.Id);
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

    [Fact]
    public async Task INSERT_ExportJob_with_foreign_UserId_raises_RlsPolicyViolation()
    {
        // WITH CHECK negative control: user B cannot stamp an ExportJob with A's
        // UserId — the download-token path must never be able to mint a job that
        // resolves to someone else's account.
        await using var appB = _fixture.CreateAppContext(_fixture.UserB);
        appB.ExportJobs.Add(new ExportJob
        {
            Id          = Guid.NewGuid(),
            UserId      = _fixture.UserA,            // foreign
            Status      = ExportJobStatus.Pending,
            Format      = ExportFormat.Zip,
            RequestedAt = DateTime.UtcNow,
            TokenLookup = Array.Empty<byte>(),
        });

        var act = async () => await appB.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>();
        ex.Which.TableName.Should().Be("ExportJobs");
        ex.Which.GucUserId.Should().Be(_fixture.UserB);
        ex.Which.OriginalException.SqlState.Should().Be("42501");
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
