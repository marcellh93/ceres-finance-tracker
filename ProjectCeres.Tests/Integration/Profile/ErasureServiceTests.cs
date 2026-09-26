using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ErasureService against the real project_ceres_test database. NOTE the transaction
/// model: TestDbFixture rollback-isolates each test inside a transaction on `_fixture.Db`,
/// so a row inserted through `Db` is visible only to `Db` (not to a separate connection).
/// RequestAsync writes the request through `Db` → assert on `_fixture.Db`. CancelAsync
/// reads through its own AdminDbContext connection (the caller is sealed, pre-auth), so
/// the CancelAsync tests SEED the request via a committed admin write — the production
/// shape, where request and cancel are separate committed HTTP requests. Reuses the
/// RecordingAuditLogWriter from ExportJobServiceTests.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ErasureServiceTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = TestDbFixture.SentinelUserId;

    private readonly TestDbFixture _fixture = new();
    private ErasureService _service = null!;
    private RecordingAuditLogWriter _audit = null!;
    private readonly ErasureTokenGenerator _tokens =
        new(new Argon2idPasswordHasher(Options.Create(new Argon2idOptions())));
    private readonly TokenLookupHasher _lookup =
        new(Options.Create(new TokenLookupOptions { Secret = Convert.ToBase64String(new byte[32]) }));

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        // Seed a real AspNetUsers row for the sentinel (the fixture seeds finance data
        // under this GUID but no user row). Committed via admin so both connections see it.
        await using (var admin = _fixture.CreateAdminContext())
        {
            if (!await admin.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == Sentinel))
            {
                admin.Users.Add(new ApplicationUser
                {
                    Id = Sentinel,
                    UserName = $"erasure-{Sentinel:N}@example.com",
                    NormalizedUserName = $"ERASURE-{Sentinel:N}@EXAMPLE.COM",
                    Email = $"erasure-{Sentinel:N}@example.com",
                    NormalizedEmail = $"ERASURE-{Sentinel:N}@EXAMPLE.COM",
                    EmailConfirmed = true,
                });
                await admin.SaveChangesAsync();
            }
        }

        _audit = new RecordingAuditLogWriter();
        _service = new ErasureService(
            _fixture.Db, _fixture.CreateAdminContext(), new FakeCurrentUserAccessor(Sentinel),
            TimeProvider.System, _tokens, _lookup, _audit);
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ErasureRequests.IgnoreQueryFilters().Where(r => r.UserId == Sentinel).ExecuteDeleteAsync();
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == Sentinel).ExecuteDeleteAsync();
        }
        await _fixture.DisposeAsync();
    }

    /// <summary>Seed a committed Sealed ErasureRequest (+ sealed user) for the CancelAsync
    /// tests, whose service reads and writes through separate committed connections.
    /// Returns the raw token.</summary>
    private async Task<string> SeedCommittedRequestAsync(ErasureStatus status = ErasureStatus.Sealed)
    {
        var raw = _tokens.Generate();
        await using var admin = _fixture.CreateAdminContext();
        admin.ErasureRequests.Add(new ErasureRequest
        {
            Id = Guid.NewGuid(),
            UserId = Sentinel,
            Status = status,
            RequestedAt = DateTime.UtcNow,
            ExecuteAfter = DateTime.UtcNow.AddHours(72),
            CancelTokenLookup = _lookup.ComputeLookup(raw),
            CancelTokenHash = _tokens.Hash(raw),
        });
        await admin.Users.IgnoreQueryFilters().Where(u => u.Id == Sentinel)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        await admin.SaveChangesAsync();
        return raw;
    }

    /// <summary>An ErasureService whose app context is a COMMITTED, RLS-bound ceres_app
    /// connection (not _fixture.Db's rolled-back isolation transaction). CancelAsync reads
    /// the candidate via admin then updates it via the app context inside its own
    /// BeginPreAuthUserScopeAsync transaction — both must see the same committed row, which
    /// _fixture.Db (trapped in the fixture's uncommitted transaction) cannot. Mirrors the
    /// production shape: request and cancel are separate committed HTTP requests.</summary>
    private ErasureService CommittedCancelService()
        => new(_fixture.CreateAppContext(new FakeCurrentUserAccessor(Sentinel)),
            _fixture.CreateAdminContext(), new FakeCurrentUserAccessor(Sentinel),
            TimeProvider.System, _tokens, _lookup, _audit);

    [Fact]
    public async Task RequestAsync_seals_the_account_creates_a_request_and_audits_once()
    {
        var (request, rawToken) = await _service.RequestAsync(CancellationToken.None);

        request.UserId.Should().Be(Sentinel);
        request.Status.Should().Be(ErasureStatus.Sealed);
        request.ExecuteAfter.Should().BeCloseTo(request.RequestedAt.AddHours(72), TimeSpan.FromSeconds(5));
        rawToken.Should().NotBeNullOrEmpty("the raw cancel token is returned for the email link");

        // The request row lives in _fixture.Db's transaction — read it back there.
        (await _fixture.Db.ErasureRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id))
            .Status.Should().Be(ErasureStatus.Sealed);
        // The seal write goes through the admin context (ceres_app has no AspNetUsers grant),
        // committed → visible to a fresh admin read.
        await using var admin = _fixture.CreateAdminContext();
        (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == Sentinel))
            .SealedAt.Should().NotBeNull("the account is sealed immediately on request");
        _audit.Recorded.Should().ContainSingle().Which.Should().Be(AuditLogAction.GdprErasureRequested);
    }

    [Fact]
    public async Task RequestAsync_called_again_dedupes_no_second_row_or_audit()
    {
        var first = await _service.RequestAsync(CancellationToken.None);
        var second = await _service.RequestAsync(CancellationToken.None);

        second.request.Id.Should().Be(first.request.Id, "an existing Sealed request is reused");
        second.rawCancelToken.Should().BeEmpty("a dedupe-return cannot re-mint the token");

        (await _fixture.Db.ErasureRequests.CountAsync(r => r.UserId == Sentinel))
            .Should().Be(1, "no second request row");
        _audit.Recorded.Should().ContainSingle("the audit row is written once, not again on dedupe");
    }

    [Fact]
    public async Task CancelAsync_with_valid_token_unseals_and_cancels()
    {
        var rawToken = await SeedCommittedRequestAsync();

        var outcome = await CommittedCancelService().CancelAsync(rawToken, CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.Cancelled>();
        await using var admin = _fixture.CreateAdminContext();
        (await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().FirstAsync(r => r.UserId == Sentinel))
            .Status.Should().Be(ErasureStatus.Cancelled);
        (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == Sentinel))
            .SealedAt.Should().BeNull("cancellation un-seals the account");
    }

    [Fact]
    public async Task CancelAsync_with_wrong_token_returns_NotFound()
    {
        await SeedCommittedRequestAsync(); // a real request exists, but with a different token

        var outcome = await CommittedCancelService().CancelAsync("this-is-not-the-token", CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.NotFound>();
        await using var admin = _fixture.CreateAdminContext();
        (await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().FirstAsync(r => r.UserId == Sentinel))
            .Status.Should().Be(ErasureStatus.Sealed, "a wrong token must not cancel");
    }

    [Fact]
    public async Task CancelAsync_on_a_completed_request_returns_Gone()
    {
        var rawToken = await SeedCommittedRequestAsync(ErasureStatus.Completed);

        var outcome = await CommittedCancelService().CancelAsync(rawToken, CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.Gone>("a Completed request is un-cancellable");
    }
}
