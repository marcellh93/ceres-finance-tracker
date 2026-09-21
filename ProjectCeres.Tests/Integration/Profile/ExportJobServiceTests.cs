using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ExportJobService against the real project_ceres_test database, under the caller's
/// own RLS scope (ceres_app) — the same connection the request pipeline uses.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ExportJobServiceTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = TestDbFixture.SentinelUserId;
    private static readonly Guid OtherUser = new("00000000-0000-0000-0000-0000000000ff");

    private readonly TestDbFixture _fixture = new();
    private ExportJobService _service = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();
        var user = new FakeCurrentUserAccessor(Sentinel);
        _service = new ExportJobService(_fixture.Db, user, TimeProvider.System);
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task CreateOrGetPendingAsync_inserts_one_pending_row_for_a_fresh_user()
    {
        var job = await _service.CreateOrGetPendingAsync(CancellationToken.None);

        job.UserId.Should().Be(Sentinel);
        job.Status.Should().Be(ExportJobStatus.Pending);
        job.Format.Should().Be(ExportFormat.Zip);

        var persisted = await _fixture.Db.ExportJobs.AsNoTracking()
            .SingleAsync(j => j.Id == job.Id);
        persisted.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task CreateOrGetPendingAsync_called_again_returns_the_same_job_no_duplicate_row()
    {
        var first = await _service.CreateOrGetPendingAsync(CancellationToken.None);

        var second = await _service.CreateOrGetPendingAsync(CancellationToken.None);

        second.Id.Should().Be(first.Id, "an existing Pending/Processing job is reused, never duplicated");
        var count = await _fixture.Db.ExportJobs.CountAsync(j => j.UserId == Sentinel);
        count.Should().Be(1, "no second row should have been inserted");
    }

    [Fact]
    public async Task FindOwnByTokenAsync_finds_the_callers_own_job_by_token_lookup()
    {
        var job = await _service.CreateOrGetPendingAsync(CancellationToken.None);
        var tokenLookup = new byte[32];
        Random.Shared.NextBytes(tokenLookup);
        job.TokenLookup = tokenLookup;
        await _fixture.Db.SaveChangesAsync();

        var found = await _service.FindOwnByTokenAsync(tokenLookup, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task FindOwnByTokenAsync_returns_null_for_another_users_token()
    {
        var tokenLookup = new byte[32];
        Random.Shared.NextBytes(tokenLookup);
        var foreignJob = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = OtherUser,
            Status = ExportJobStatus.Ready,
            Format = ExportFormat.Zip,
            RequestedAt = DateTime.UtcNow,
            TokenLookup = tokenLookup,
        };
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.ExportJobs.Add(foreignJob);
            await admin.SaveChangesAsync();
        }

        try
        {
            var found = await _service.FindOwnByTokenAsync(tokenLookup, CancellationToken.None);

            found.Should().BeNull("RLS makes another user's row invisible to the caller's own scope");
        }
        finally
        {
            await using var admin = _fixture.CreateAdminContext();
            await admin.ExportJobs.Where(j => j.Id == foreignJob.Id).ExecuteDeleteAsync();
        }
    }
}
