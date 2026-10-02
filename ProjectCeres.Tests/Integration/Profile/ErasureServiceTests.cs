using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ErasureService against the real project_ceres_test database.
///
/// <para><b>Isolation — why NOT the sentinel.</b> This suite creates its OWN dedicated
/// AspNetUsers row under a fresh GUID (never <see cref="TestDbFixture.SentinelUserId"/>),
/// because RequestAsync seals the account and CancelAsync un-seals it — both mutate an
/// AspNetUsers row. The sentinel is the shared finance-fixture identity with NO AspNetUsers
/// row by design; giving it one with an @example email makes UserOwnedCleanup's abandoned-user
/// sweep treat it as reclaimable and PURGE its irreplaceable fixture Accounts (they are
/// migration-seeded once and restorable from no migration). This suite therefore owns a
/// throwaway user it fully purges on teardown via UserOwnedCleanup.PurgeUserAsync.</para>
///
/// <para><b>Transaction model.</b> TestDbFixture rollback-isolates each test inside a
/// transaction on <c>_fixture.Db</c>, so a row written through <c>Db</c> is visible only to
/// <c>Db</c>. RequestAsync writes through the app context; its assertions read back on the
/// SAME context. CancelAsync reads its candidate through a separate committed AdminDbContext
/// connection (the caller is sealed, pre-auth), so the CancelAsync tests seed the request via
/// a committed admin write and drive the service on a COMMITTED app context — the production
/// shape, where request and cancel are separate committed HTTP requests.</para>
/// Reuses the RecordingAuditLogWriter from ExportJobServiceTests.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ErasureServiceTests : IAsyncLifetime
{
    // A throwaway user, unique per test-class instance — NEVER the shared sentinel fixture.
    // @erasure-test.invalid so UserOwnedCleanup's sweep can safely reclaim it if teardown is missed.
    private readonly Guid _userId = Guid.NewGuid();

    private readonly TestDbFixture _fixture = new();
    private AppDbContext _appDb = null!;
    private ErasureService _service = null!;
    private RecordingAuditLogWriter _audit = null!;
    private readonly List<EmailMessage> _sentEmails = [];
    private readonly Argon2idPasswordHasher _argon = new(Options.Create(new Argon2idOptions()));
    private readonly ErasureTokenGenerator _tokens =
        new(new Argon2idPasswordHasher(Options.Create(new Argon2idOptions())));
    private readonly TokenLookupHasher _lookup =
        new(Options.Create(new TokenLookupOptions { Secret = Convert.ToBase64String(new byte[32]) }));
    private IStringLocalizer<EmailsResource> _localizer = null!;
    private ServiceProvider _localizationProvider = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        // Commit the throwaway user so the service's admin/app contexts (separate connections)
        // both see it. RequestAsync's app context is RLS-bound to _userId (below), so it can
        // write the ErasureRequest row past the user_isolation WITH CHECK policy.
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.Users.Add(new ApplicationUser
            {
                Id = _userId,
                UserName = $"erasure-{_userId:N}@erasure-test.invalid",
                NormalizedUserName = $"ERASURE-{_userId:N}@ERASURE-TEST.INVALID",
                Email = $"erasure-{_userId:N}@erasure-test.invalid",
                NormalizedEmail = $"ERASURE-{_userId:N}@ERASURE-TEST.INVALID",
                EmailConfirmed = true,
            });
            await admin.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        _localizationProvider = services.BuildServiceProvider();
        _localizer = _localizationProvider.GetRequiredService<IStringLocalizer<EmailsResource>>();

        _appDb = _fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId));
        _audit = new RecordingAuditLogWriter();
        _service = BuildService(_appDb);
    }

    private ErasureService BuildService(AppDbContext appDb) => new(
        appDb, _fixture.CreateAdminContext(), new FakeCurrentUserAccessor(_userId),
        TimeProvider.System, _tokens, _lookup, _argon, _audit,
        new EmailComposer(_localizer), new CapturingEmailService(_sentEmails),
        new EmailRecipientResolver(_fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId))),
        new LanguageResolver(_fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId))),
        Options.Create(new EmailOptions { PublicBaseUrl = "https://ceres.invalid" }),
        // Configured PublicBaseUrl above always wins over the HttpContext fallback in
        // this suite, so a null-context mock is never actually dereferenced — see the
        // dedicated fallback test below, which configures no PublicBaseUrl on purpose.
        Mock.Of<IHttpContextAccessor>(),
        NullLogger<ErasureService>.Instance);

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ErasureRequests.IgnoreQueryFilters().Where(r => r.UserId == _userId).ExecuteDeleteAsync();
            // Purge every user-owned row then the user itself — the same reclaim path the sweep uses.
            await UserOwnedCleanup.PurgeUserAsync(admin, _userId);
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
        }
        await _appDb.DisposeAsync();
        await _fixture.DisposeAsync();
        _localizationProvider.Dispose();
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
            UserId = _userId,
            Status = status,
            RequestedAt = DateTime.UtcNow,
            ExecuteAfter = DateTime.UtcNow.AddHours(72),
            CancelTokenLookup = _lookup.ComputeLookup(raw),
            CancelTokenHash = _tokens.Hash(raw),
        });
        await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        await admin.SaveChangesAsync();
        return raw;
    }

    /// <summary>An ErasureService whose app context is a COMMITTED, RLS-bound ceres_app
    /// connection (not _appDb, which is fine here too, but a fresh one keeps each cancel
    /// test's scope clean). CancelAsync reads the candidate via admin then updates it via
    /// the app context inside its own BeginPreAuthUserScopeAsync transaction — both see the
    /// committed seeded row. Mirrors production: request and cancel are separate requests.</summary>
    private ErasureService CommittedCancelService()
        => BuildService(_fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId)));

    [Fact]
    public async Task RequestAsync_seals_the_account_creates_a_request_and_audits_once()
    {
        var (request, rawToken) = await _service.RequestAsync(CancellationToken.None);

        request.UserId.Should().Be(_userId);
        request.Status.Should().Be(ErasureStatus.Sealed);
        request.ExecuteAfter.Should().BeCloseTo(request.RequestedAt.AddHours(72), TimeSpan.FromSeconds(5));
        rawToken.Should().NotBeNullOrEmpty("the raw cancel token is returned for the email link");

        // The request row was written through _appDb — read it back on the same context.
        (await _appDb.ErasureRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id))
            .Status.Should().Be(ErasureStatus.Sealed);
        // The seal write goes through the admin context (ceres_app has no AspNetUsers grant),
        // committed → visible to a fresh admin read.
        await using var admin = _fixture.CreateAdminContext();
        (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == _userId))
            .SealedAt.Should().NotBeNull("the account is sealed immediately on request");
        _audit.Recorded.Should().ContainSingle().Which.Should().Be(AuditLogAction.GdprErasureRequested);
    }

    [Fact]
    public async Task RequestAsync_sends_GdprErasureInitiated_email_with_cancel_link()
    {
        var (_, rawToken) = await _service.RequestAsync(CancellationToken.None);

        _sentEmails.Should().ContainSingle();
        var sent = _sentEmails[0];
        await using var admin = _fixture.CreateAdminContext();
        var expectedEmail = (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == _userId)).Email;
        sent.To.Address.Should().Be(expectedEmail);
        sent.BodyText.Should().Contain(rawToken,
            "the cancel link must carry the raw base64url token in the fragment, unescaped, exactly as EmailChangeService's revokeUrl does");
        sent.BodyText.Should().Contain("/erasure/cancel#token=",
            "the link must point at the SPA route (which renders ErasureCancel.tsx and POSTs the token from the fragment), " +
            "not the raw API endpoint — mirroring EmailChangeService's /email-change/revoke#token= shape");
    }

    [Fact]
    public async Task RequestAsync_falls_back_to_the_request_host_when_PublicBaseUrl_is_unset()
    {
        // Email:PublicBaseUrl is unset by design outside Production (Program.cs's startup
        // check only enforces it IsProduction()) — the real E2E/dev environment exercises
        // THIS branch, not the configured one every other test in this file pins. Caught
        // live: an E2E run against a real server produced a bare "/erasure/cancel#token="
        // with no host at all before this fallback existed, which a mocked-options unit
        // test alone could never have surfaced (mocks assume the config value is present).
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("erasure-fallback.invalid");
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.SetupGet(h => h.HttpContext).Returns(httpContext);

        var sentEmails = new List<EmailMessage>();
        await using var appDb = _fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId));
        var service = new ErasureService(
            appDb, _fixture.CreateAdminContext(), new FakeCurrentUserAccessor(_userId),
            TimeProvider.System, _tokens, _lookup, _argon, _audit,
            new EmailComposer(_localizer), new CapturingEmailService(sentEmails),
            new EmailRecipientResolver(_fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId))),
            new LanguageResolver(_fixture.CreateAppContext(new FakeCurrentUserAccessor(_userId))),
            Options.Create(new EmailOptions()), // PublicBaseUrl deliberately unset
            httpAccessor.Object,
            NullLogger<ErasureService>.Instance);

        await service.RequestAsync(CancellationToken.None);

        sentEmails.Should().ContainSingle();
        sentEmails[0].BodyText.Should().Contain("https://erasure-fallback.invalid/erasure/cancel#token=",
            "with no configured PublicBaseUrl, the cancel link must be built from the live request's scheme+host");
    }

    [Fact]
    public async Task RequestAsync_called_again_dedupes_no_second_row_or_audit()
    {
        var first = await _service.RequestAsync(CancellationToken.None);
        var second = await _service.RequestAsync(CancellationToken.None);

        second.request.Id.Should().Be(first.request.Id, "an existing Sealed request is reused");
        second.rawCancelToken.Should().BeEmpty("a dedupe-return cannot re-mint the token");

        (await _appDb.ErasureRequests.CountAsync(r => r.UserId == _userId))
            .Should().Be(1, "no second request row");
        _audit.Recorded.Should().ContainSingle("the audit row is written once, not again on dedupe");
    }

    [Fact]
    public async Task CancelAsync_with_valid_token_unseals_cancels_and_audits()
    {
        var rawToken = await SeedCommittedRequestAsync();

        var outcome = await CommittedCancelService().CancelAsync(rawToken, CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.Cancelled>();
        await using var admin = _fixture.CreateAdminContext();
        (await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().FirstAsync(r => r.UserId == _userId))
            .Status.Should().Be(ErasureStatus.Cancelled);
        (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == _userId))
            .SealedAt.Should().BeNull("cancellation un-seals the account");
        _audit.Recorded.Should().ContainSingle().Which.Should().Be(AuditLogAction.GdprErasureCancelled);
    }

    [Fact]
    public async Task CancelAsync_with_wrong_token_returns_NotFound_and_leaves_the_request_Sealed()
    {
        await SeedCommittedRequestAsync(); // a real request exists, but with a different token

        var outcome = await CommittedCancelService().CancelAsync("this-is-not-the-token", CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.NotFound>();
        await using var admin = _fixture.CreateAdminContext();
        (await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().FirstAsync(r => r.UserId == _userId))
            .Status.Should().Be(ErasureStatus.Sealed, "a wrong token must not cancel");
        (await admin.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == _userId))
            .SealedAt.Should().NotBeNull("a wrong token must not un-seal");
        _audit.Recorded.Should().BeEmpty("a rejected cancel writes no audit row");
    }

    [Fact]
    public async Task CancelAsync_with_empty_token_returns_NotFound()
    {
        await SeedCommittedRequestAsync();

        var outcome = await CommittedCancelService().CancelAsync("   ", CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.NotFound>("an empty/whitespace token is never a match");
        await using var admin = _fixture.CreateAdminContext();
        (await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking().FirstAsync(r => r.UserId == _userId))
            .Status.Should().Be(ErasureStatus.Sealed);
    }

    [Fact]
    public async Task CancelAsync_on_a_completed_request_returns_Gone()
    {
        var rawToken = await SeedCommittedRequestAsync(ErasureStatus.Completed);

        var outcome = await CommittedCancelService().CancelAsync(rawToken, CancellationToken.None);

        outcome.Should().BeOfType<ErasureCancelOutcome.Gone>("a Completed request is un-cancellable");
    }
}
