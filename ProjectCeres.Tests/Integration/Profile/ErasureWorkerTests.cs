using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ErasureWorker.ProcessEligibleAsync against the real project_ceres_test database.
/// Mirrors ExportJobWorkerTests/ErasureExecutorTests: seeds via a throwaway user on
/// a separate AdminDbContext connection, since the worker reads/writes through
/// AdminDbContext (BYPASSRLS) — the SweepSessions/ExportJobWorker pattern. Stage 13.9 Task 8.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ErasureWorkerTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private readonly Guid _userId = Guid.NewGuid();
    private string _contentRoot = null!;
    private ErasureExecutor _executor = null!;

    private const string RealEmail = "erasure-worker-test@erasure-test.invalid";

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _contentRoot = Path.Combine(Path.GetTempPath(), $"ceres-erasure-worker-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        var lookup = new TokenLookupHasher(Options.Create(new TokenLookupOptions
        {
            Secret = Convert.ToBase64String(new byte[32]),
        }));

        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Users.Add(new ApplicationUser
            {
                Id = _userId,
                UserName = RealEmail,
                NormalizedUserName = RealEmail.ToUpperInvariant(),
                Email = RealEmail,
                NormalizedEmail = RealEmail.ToUpperInvariant(),
                EmailConfirmed = true,
            });
            await admin.SaveChangesAsync();
        }

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_contentRoot);

        _executor = new ErasureExecutor(
            _fixture.CreateAdminContext(), new ErasurePseudonym(lookup), env.Object, TimeProvider.System,
            new LowercaseLookupNormalizer(), lookup, NullLogger<ErasureExecutor>.Instance);
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ErasureRequests.IgnoreQueryFilters().Where(r => r.UserId == _userId).ExecuteDeleteAsync();
            await UserOwnedCleanup.PurgeUserAsync(admin, _userId);
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
        }

        await _fixture.DisposeAsync();

        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
    }

    private async Task<Guid> SeedRequestAsync(ErasureStatus status, DateTime executeAfter)
    {
        var id = Guid.NewGuid();
        await using var admin = _fixture.CreateAdminContext();
        admin.ErasureRequests.Add(new ErasureRequest
        {
            Id = id,
            UserId = _userId,
            Status = status,
            RequestedAt = DateTime.UtcNow.AddHours(-73),
            ExecuteAfter = executeAfter,
            // IX_ErasureRequests_CancelTokenLookup is a GLOBAL unique index — a fixed
            // all-zero value here would collide against ANY other row (this class's
            // own other tests, ErasureExecutorTests, or an abandoned prior run) that
            // seeded the same constant. Random per call, matching a real token.
            CancelTokenLookup = Guid.NewGuid().ToByteArray(),
            CancelTokenHash = "unused",
        });
        await admin.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task ProcessEligibleAsync_executes_a_sealed_request_past_its_window()
    {
        await SeedRequestAsync(ErasureStatus.Sealed, DateTime.UtcNow.AddHours(-1));

        var touched = await ErasureWorker.ProcessEligibleAsync(
            _fixture.CreateAdminContext(), _executor, TimeProvider.System,
            NullLogger.Instance, CancellationToken.None);

        touched.Should().Be(1);

        await using var admin = _fixture.CreateAdminContext();
        var request = await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Completed, "an eligible Sealed request must be executed");
        request.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessEligibleAsync_skips_a_sealed_request_still_within_its_window()
    {
        await SeedRequestAsync(ErasureStatus.Sealed, DateTime.UtcNow.AddHours(1));

        var touched = await ErasureWorker.ProcessEligibleAsync(
            _fixture.CreateAdminContext(), _executor, TimeProvider.System,
            NullLogger.Instance, CancellationToken.None);

        touched.Should().Be(0, "a request still within its 72h cancel window must not be executed");

        await using var admin = _fixture.CreateAdminContext();
        var request = await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Sealed, "the request must remain untouched");

        var user = await admin.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == _userId);
        user.Email.Should().Be(RealEmail, "the user's data must be untouched — no erasure side effects");
        user.ErasedAt.Should().BeNull();
    }

    [Fact]
    public async Task ProcessEligibleAsync_never_executes_a_cancelled_request_past_its_window()
    {
        await SeedRequestAsync(ErasureStatus.Cancelled, DateTime.UtcNow.AddHours(-1));

        var touched = await ErasureWorker.ProcessEligibleAsync(
            _fixture.CreateAdminContext(), _executor, TimeProvider.System,
            NullLogger.Instance, CancellationToken.None);

        touched.Should().Be(0, "the query itself excludes non-Sealed requests — a Cancelled row must never reach the executor");

        await using var admin = _fixture.CreateAdminContext();
        var request = await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Cancelled, "status must be left exactly as seeded");
    }

    [Fact]
    public async Task ProcessEligibleAsync_skips_a_request_cancelled_after_the_sweep_query_but_before_the_per_item_recheck()
    {
        // The genuine race Task 10 names: the sweep's outer query (line "eligible =
        // ...ToListAsync") and the per-item re-check are two SEPARATE round-trips —
        // a user can cancel in the gap between them. The sibling test above only
        // proves a row Cancelled BEFORE the sweep starts is excluded by the outer
        // WHERE clause; it never exercises the re-check at all. This test seeds
        // Sealed (so the row genuinely enters the eligible set), waits for a
        // separate connection to flip it to Cancelled, then calls
        // ProcessEligibleAsync — proving the re-check (not the outer query) is what
        // actually stops execution here.
        await SeedRequestAsync(ErasureStatus.Sealed, DateTime.UtcNow.AddHours(-1));

        await using (var admin = _fixture.CreateAdminContext())
        {
            var confirmedEligible = await admin.ErasureRequests.IgnoreQueryFilters()
                .Where(r => r.UserId == _userId && r.Status == ErasureStatus.Sealed
                    && r.ExecuteAfter <= DateTime.UtcNow)
                .AnyAsync();
            confirmedEligible.Should().BeTrue("the row must genuinely be in the eligible set for this test to prove anything");

            // Race: cancel on a separate connection, simulating the user's cancel
            // link landing between the sweep's outer query and this row's turn in
            // the loop.
            await admin.ErasureRequests.IgnoreQueryFilters()
                .Where(r => r.UserId == _userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ErasureStatus.Cancelled)
                    .SetProperty(r => r.CancelledAt, DateTime.UtcNow));
        }

        var touched = await ErasureWorker.ProcessEligibleAsync(
            _fixture.CreateAdminContext(), _executor, TimeProvider.System,
            NullLogger.Instance, CancellationToken.None);

        touched.Should().Be(0, "the per-item re-check must catch the cancel that landed after the outer sweep query ran");

        await using var check = _fixture.CreateAdminContext();
        var request = await check.ErasureRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.UserId == _userId);
        request.Status.Should().Be(ErasureStatus.Cancelled, "the row must be left exactly as the race left it, not re-flipped by the executor");

        var user = await check.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == _userId);
        user.Email.Should().Be(RealEmail, "a cancel that races the sweep must still fully protect the user's data");
    }
}
