using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Integration.Profile;

/// <summary>
/// ExportJobWorker.ProcessPendingAsync against the real project_ceres_test database.
/// Mirrors DataExportBuilderTests: seeds via _fixture.CreateAdminContext() directly
/// (a separate, non-transactional connection), since the worker itself reads/writes
/// through AdminDbContext (BYPASSRLS), the SweepSessions pattern. Stage 13.8 Task 6.
/// </summary>
[Collection("TestDbFixtureTests")]
public class ExportJobWorkerTests : IAsyncLifetime
{
    private readonly TestDbFixture _fixture = new();
    private readonly Guid _userId = Guid.NewGuid();
    private string _contentRoot = null!;
    private IStringLocalizer<EmailsResource> _localizer = null!;
    private ServiceProvider _localizationProvider = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _contentRoot = Path.Combine(Path.GetTempPath(), $"ceres-export-worker-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        _localizationProvider = services.BuildServiceProvider();
        _localizer = _localizationProvider.GetRequiredService<IStringLocalizer<EmailsResource>>();

        await using var admin = _fixture.CreateAdminContext();
        admin.Users.Add(new ApplicationUser
        {
            Id = _userId,
            UserName = $"export-worker-{_userId:N}@example.com",
            NormalizedUserName = $"EXPORT-WORKER-{_userId:N}@EXAMPLE.COM",
            Email = $"export-worker-{_userId:N}@example.com",
            NormalizedEmail = $"EXPORT-WORKER-{_userId:N}@EXAMPLE.COM",
            EmailConfirmed = true,
        });
        admin.Settings.Add(new Settings
        {
            UserId = _userId,
            NumberFormat = "en-US",
            DateFormat = "yyyy-MM-dd",
            DefaultCurrencyId = 1,
            PeriodStartDay = 1,
            Language = "en",
        });
        await admin.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var admin = _fixture.CreateAdminContext())
        {
            await admin.ExportJobs.IgnoreQueryFilters().Where(j => j.UserId == _userId).ExecuteDeleteAsync();
            await admin.Settings.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
            await admin.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
        }

        await _fixture.DisposeAsync();
        _localizationProvider.Dispose();

        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
    }

    private IEmailComposer Composer() => new EmailComposer(_localizer);

    private IEmailRecipientResolver Recipients(ProjectCeres.Data.AdminDbContext db) => new EmailRecipientResolver(db);

    private ILanguageResolver Languages(ProjectCeres.Data.AdminDbContext db) => new LanguageResolver(db);

    private static IOptions<FileAttachmentOptions> AttachmentOptions(string root) =>
        Options.Create(new FileAttachmentOptions { RootPath = root });

    private static IOptions<EmailOptions> EmailOpts() =>
        Options.Create(new EmailOptions { PublicBaseUrl = "https://ceres.invalid" });

    private static ExportTokenGenerator TokenGenerator() =>
        new(new Argon2idPasswordHasher(Options.Create(new Argon2idOptions())));

    private static TokenLookupHasher LookupHasher() =>
        new(Options.Create(new TokenLookupOptions { Secret = Convert.ToBase64String(new byte[32]) }));

    private static Mock<IWebHostEnvironment> Env(string contentRoot)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(contentRoot);
        return env;
    }

    private async Task<ExportJob> SeedJobAsync(ExportJobStatus status, DateTime? expiresAt = null,
        DateTime? consumedAt = null, DateTime? emailedAt = null, string? storedPath = null, int failureCount = 0)
    {
        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Status = status,
            Format = ExportFormat.Zip,
            RequestedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt,
            EmailedAt = emailedAt,
            StoredPath = storedPath,
            FailureCount = failureCount,
        };
        await using var admin = _fixture.CreateAdminContext();
        admin.ExportJobs.Add(job);
        await admin.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task ProcessPendingAsync_builds_a_pending_job_tokenizes_and_emails_it()
    {
        await SeedJobAsync(ExportJobStatus.Pending);
        var captured = new List<EmailMessage>();

        await using var db = _fixture.CreateAdminContext();
        var builder = new DataExportBuilder(_fixture.CreateAdminContext(), Env(_contentRoot).Object, AttachmentOptions(_contentRoot));
        var tokens = TokenGenerator();
        var lookupHasher = LookupHasher();

        await ExportJobWorker.ProcessPendingAsync(
            db, builder, tokens, lookupHasher, Composer(), new CapturingEmailService(captured),
            Recipients(db), Languages(db), EmailOpts(), AttachmentOptions(_contentRoot), Env(_contentRoot).Object,
            TimeProvider.System, NullLoggerInstance(), CancellationToken.None);

        var persisted = await FindOwnAsync();
        persisted.Status.Should().Be(ExportJobStatus.Ready);
        persisted.TokenLookup.Should().NotBeEmpty();
        persisted.TokenHash.Should().NotBeNullOrEmpty();
        persisted.StoredPath.Should().NotBeNullOrEmpty();
        File.Exists(persisted.StoredPath!).Should().BeTrue();
        persisted.ReadyAt.Should().NotBeNull();
        persisted.ExpiresAt.Should().NotBeNull();
        persisted.EmailedAt.Should().NotBeNull();

        captured.Should().HaveCount(1);
        var sentUrl = ExtractToken(captured[0].BodyText);
        sentUrl.Should().NotBeNullOrEmpty("the email body must carry the raw download token");

        var hasher = new Argon2idPasswordHasher(Options.Create(new Argon2idOptions()));
        var verifyResult = hasher.VerifyHashedPassword(new ApplicationUser(), persisted.TokenHash, sentUrl!);
        (verifyResult is Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success
            or Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded)
            .Should().BeTrue("the stored TokenHash must verify against the raw token the email carried");
        lookupHasher.ComputeLookup(sentUrl!).Should().BeEquivalentTo(persisted.TokenLookup,
            "TokenLookup must be the HMAC of the same raw token the email carried");
    }

    [Fact]
    public async Task ProcessPendingAsync_resends_a_ready_unemailed_job_without_rebuilding()
    {
        var zipPath = Path.Combine(_contentRoot, "exports", $"export-{_userId:N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        await File.WriteAllTextAsync(zipPath, "stub-zip-contents");
        var originalWriteTime = File.GetLastWriteTimeUtc(zipPath);

        await SeedJobAsync(ExportJobStatus.Ready, expiresAt: DateTime.UtcNow.AddHours(23), storedPath: zipPath, emailedAt: null);
        var captured = new List<EmailMessage>();

        await using var db = _fixture.CreateAdminContext();
        // Never invoked in this path (a Ready+unemailed job re-sends without rebuilding) —
        // the mtime assertion below is the real proof, not a mock invocation count, since
        // DataExportBuilder is a sealed concrete class with no seam to intercept calls.
        var builder = new DataExportBuilder(_fixture.CreateAdminContext(), Env(_contentRoot).Object, AttachmentOptions(_contentRoot));
        var tokens = TokenGenerator();
        var lookupHasher = LookupHasher();

        await ExportJobWorker.ProcessPendingAsync(
            db, builder, tokens, lookupHasher, Composer(), new CapturingEmailService(captured),
            Recipients(db), Languages(db), EmailOpts(), AttachmentOptions(_contentRoot), Env(_contentRoot).Object,
            TimeProvider.System, NullLoggerInstance(), CancellationToken.None);

        var persisted = await FindOwnAsync();
        persisted.EmailedAt.Should().NotBeNull("the resend must stamp EmailedAt");
        persisted.StoredPath.Should().Be(zipPath, "the resend must not touch StoredPath — no rebuild happened");
        File.GetLastWriteTimeUtc(zipPath).Should().Be(originalWriteTime, "the ZIP must not have been rewritten");
        captured.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_retries_a_failing_build_and_fails_after_three_attempts()
    {
        // failureCount starts at 2 so this single run's failure is the 3rd (terminal) one.
        await SeedJobAsync(ExportJobStatus.Pending, failureCount: 2);
        var captured = new List<EmailMessage>();

        // Force BuildAsync to throw for real (no builder interface exists to fake):
        // place a regular FILE at the exact path the worker computes for the "exports"
        // directory, so DataExportBuilder's Directory.CreateDirectory(outputDir) throws.
        var exportsPath = Path.Combine(_contentRoot, "exports");
        await File.WriteAllTextAsync(exportsPath, "not-a-directory");

        // Seed a partial ZIP the worker must delete on the terminal failure. It lives
        // outside the blocked "exports" path so cleanup itself doesn't collide with it.
        var partialDir = Path.Combine(_contentRoot, "partial-scratch");
        Directory.CreateDirectory(partialDir);
        var partialPath = Path.Combine(partialDir, $"export-{_userId:N}.zip");
        await File.WriteAllTextAsync(partialPath, "partial");
        await using (var admin = _fixture.CreateAdminContext())
        {
            var job = await admin.ExportJobs.IgnoreQueryFilters().SingleAsync(j => j.UserId == _userId);
            job.StoredPath = partialPath;
            await admin.SaveChangesAsync();
        }

        await using var db = _fixture.CreateAdminContext();
        var builder = new DataExportBuilder(_fixture.CreateAdminContext(), Env(_contentRoot).Object, AttachmentOptions(_contentRoot));

        await ExportJobWorker.ProcessPendingAsync(
            db, builder, TokenGenerator(), LookupHasher(), Composer(),
            new CapturingEmailService(captured), Recipients(db), Languages(db), EmailOpts(),
            AttachmentOptions(_contentRoot), Env(_contentRoot).Object, TimeProvider.System,
            NullLoggerInstance(), CancellationToken.None);

        var persisted = await FindOwnAsync();
        persisted.FailureCount.Should().Be(3);
        persisted.Status.Should().Be(ExportJobStatus.Failed);
        persisted.StoredPath.Should().BeNull("a partial ZIP must never be left referenced");
        File.Exists(partialPath).Should().BeFalse("the partial ZIP must be deleted on final failure");
        captured.Should().HaveCount(1, "a 'please retry' email must be sent on the terminal failure");
    }

    [Fact]
    public async Task ProcessPendingAsync_cleans_up_a_job_past_expiry()
    {
        var zipPath = Path.Combine(_contentRoot, "exports", $"export-{_userId:N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        await File.WriteAllTextAsync(zipPath, "expired-zip");

        await SeedJobAsync(ExportJobStatus.Ready, expiresAt: DateTime.UtcNow.AddHours(-1), storedPath: zipPath, emailedAt: DateTime.UtcNow.AddHours(-25));

        await using var db = _fixture.CreateAdminContext();
        var builder = new ProjectCeres.Services.DataExportBuilder(_fixture.CreateAdminContext(), Env(_contentRoot).Object, AttachmentOptions(_contentRoot));

        await ExportJobWorker.ProcessPendingAsync(
            db, builder, TokenGenerator(), LookupHasher(), Composer(), new CapturingEmailService([]),
            Recipients(db), Languages(db), EmailOpts(), AttachmentOptions(_contentRoot), Env(_contentRoot).Object,
            TimeProvider.System, NullLoggerInstance(), CancellationToken.None);

        var persisted = await FindOwnAsync();
        persisted.StoredPath.Should().BeNull();
        File.Exists(zipPath).Should().BeFalse("the expired ZIP must be deleted from disk");
    }

    private async Task<ExportJob> FindOwnAsync()
    {
        await using var admin = _fixture.CreateAdminContext();
        return await admin.ExportJobs.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.UserId == _userId);
    }

    private static string? ExtractToken(string bodyText)
    {
        const string marker = "token=";
        var idx = bodyText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var end = start;
        while (end < bodyText.Length && !char.IsWhiteSpace(bodyText[end])) end++;
        return Uri.UnescapeDataString(bodyText[start..end]);
    }

    private static Microsoft.Extensions.Logging.ILogger NullLoggerInstance() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
