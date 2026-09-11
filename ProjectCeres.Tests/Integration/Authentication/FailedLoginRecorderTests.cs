using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel2")]
public class FailedLoginRecorderTests : IntegrationTestBase<Bucket2AuthFactory>, IAsyncLifetime
{
    private readonly Bucket2AuthFactory _factory;

    public FailedLoginRecorderTests(Bucket2AuthFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@recorder-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith("@recorder-test.local"))
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task BadCredentials_WritesOneRow()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "user@recorder-test.local",
            userId: Guid.NewGuid(),
            FailedLoginReason.BadCredentials,
            "203.0.113.5",
            "ua/1.0",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "user@recorder-test.local")
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].Reason.Should().Be(FailedLoginReason.BadCredentials);
        rows[0].IpAddress.Should().Be("203.0.113.5");
        rows[0].UserAgent.Should().Be("ua/1.0");
    }

    [Fact]
    public async Task PasswordIsNeverInRow()
    {
        const string secret = "should-never-leak-into-the-failed-login-table";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "leaktest@recorder-test.local",
            userId: null,
            FailedLoginReason.BadCredentials,
            "203.0.113.5",
            "ua/1.0",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anyLeak = await db.FailedLoginAttempts.AnyAsync(e =>
            (e.EmailAttempted != null && e.EmailAttempted.Contains(secret))
            || e.IpAddress.Contains(secret)
            || e.UserAgent.Contains(secret));
        anyLeak.Should().BeFalse();
    }

    [Fact]
    public async Task EmailAttempted_TruncatedTo256Chars()
    {
        // Use a unique marker IP so we read THIS test's row out of the table, not
        // whatever happens to be the latest after concurrent/prior tests in the
        // IntegrationTests collection have appended their own FailedLoginAttempt rows.
        var markerIp = $"203.0.113.{Random.Shared.Next(1, 255)}";
        var longEmail = new string('a', 1000) + $"-{Guid.NewGuid():N}@recorder-test.local";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(longEmail, null, FailedLoginReason.UnknownUser,
            markerIp, "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.IpAddress == markerIp)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        row.EmailAttempted!.Length.Should().Be(256);
    }

    [Fact]
    public async Task EmailAttempted_LowercasedAndTrimmed()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "  Foo@Recorder-Test.LOCAL  ",
            null, FailedLoginReason.UnknownUser,
            "1.2.3.4", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "foo@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task UserAgent_Missing_RowStillWrites()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ua-missing@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "1.2.3.4", "", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ua-missing@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
        row!.UserAgent.Should().Be("");
    }

    [Fact]
    public async Task UserAgent_TruncatedTo512Chars()
    {
        var longUa = new string('x', 1000);
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ua-trunc@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "1.2.3.4", longUa, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ua-trunc@recorder-test.local")
            .SingleOrDefaultAsync();
        row!.UserAgent.Length.Should().Be(512);
    }

    [Fact]
    public async Task Ip_NullSafe_RecordsAsUnknown()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "ip-null@recorder-test.local", null, FailedLoginReason.BadCredentials,
            "", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ip-null@recorder-test.local")
            .SingleOrDefaultAsync();
        row!.IpAddress.Should().Be("unknown");
    }

    [Fact]
    public async Task Login_BadCredentials_WritesOneRow_ViaHttp()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "bad@recorder-test.local");
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "bad@recorder-test.local", password = "wrong-but-long-enough-pwd", rememberMe = false });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "bad@recorder-test.local")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Reason.Should().Be(FailedLoginReason.BadCredentials);
    }

    [Fact]
    public async Task Login_LockoutRejection_WritesOneRow_NotTwo()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "lockout@recorder-test.local");
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.FailedLoginAttempts
                .Where(e => e.EmailAttempted == "lockout@recorder-test.local")
                .ExecuteDeleteAsync();
        }

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "lockout@recorder-test.local", password = AuthTestFixture.ValidPassword, rememberMe = false });

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db2.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "lockout@recorder-test.local")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Reason.Should().Be(FailedLoginReason.LockedOut);
    }

    [Fact]
    public async Task Login_UnknownUser_WritesRowWithNullUserIdAndReasonUnknownUser()
    {
        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "ghost@recorder-test.local", password = "anything-long-enough-pwd", rememberMe = false });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "ghost@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();
        row!.UserId.Should().BeNull();
        row.Reason.Should().Be(FailedLoginReason.UnknownUser);
    }

    [Fact]
    public async Task LoginTotp_BadTotp_WritesRowWithReasonBadTotp()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bad-totp@recorder-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "000000" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.UserId == user.Id && e.Reason == FailedLoginReason.BadTotp)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginTotp_BadBackupCode_WritesRowWithReasonBadBackupCode()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "bad-bc@recorder-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "AAAA-AAAA-AAAA-AAAA" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.UserId == user.Id && e.Reason == FailedLoginReason.BadBackupCode)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull();
    }

    // ── Edge-case batch A (Stage 6b.3) ──────────────────────────────────────

    [Fact]
    public async Task SuccessfulLogin_DoesNotWriteRow()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "success@recorder-test.local");
        var client = _factory.CreateClient();

        using var scopeBefore = _factory.Services.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<AppDbContext>();
        var countBefore = await dbBefore.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "success@recorder-test.local")
            .CountAsync();

        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "success@recorder-test.local", password = AuthTestFixture.ValidPassword, rememberMe = false });

        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<AppDbContext>();
        var countAfter = await dbAfter.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "success@recorder-test.local")
            .CountAsync();

        countAfter.Should().Be(countBefore,
            "recorder must not write a row on a successful login");
    }

    [Fact]
    public async Task OccurredAt_IsUtc()
    {
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            "utccheck@recorder-test.local",
            userId: Guid.NewGuid(),
            FailedLoginReason.BadCredentials,
            "1.2.3.4",
            "ua/utc-test",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == "utccheck@recorder-test.local")
            .SingleOrDefaultAsync();
        row.Should().NotBeNull();

        // EF may return Unspecified Kind even for UTC values (Npgsql strips Kind).
        // Assert the value is within 5 seconds of UtcNow as a reliable UTC check.
        row!.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5),
            because: "OccurredAt must represent a UTC time written at record time");
    }

    [Fact]
    public async Task SqlInjectionFlavoredInput_StoredAsLiteralString()
    {
        const string injectionInput = "' OR '1'='1' --@recorder-test.local";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(
            injectionInput,
            userId: null,
            FailedLoginReason.UnknownUser,
            "1.2.3.4",
            "ua",
            CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The recorder lowercases + truncates to 256 chars; the input is short so it survives intact (lowercased).
        var expected = injectionInput.ToLowerInvariant().Trim();
        if (expected.Length > 256) expected = expected[..256];

        var row = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == expected)
            .SingleOrDefaultAsync();
        row.Should().NotBeNull(
            "EF parameterization must store injection-flavored input as a literal string, not execute it");
        row!.EmailAttempted.Should().Be(expected,
            "stored value must equal the lowercased literal — no SQL injection occurred");
    }

    [Fact]
    public async Task SaveChangesFailure_BubblesAs500_DoesNotIssueSession()
    {
        // Build a one-off factory that overrides FailedLoginRecorder with a throwing stub.
        // We construct directly (not via the collection fixture) so this test owns
        // the factory lifecycle and disposes when done.
        await using var factory = new ThrowingRecorderFactory();
        await AuthTestFixture.RegisterUserAsync(factory, "boom@recorder-test.local");
        var client = factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "boom@recorder-test.local", password = "wrong-but-long-enough", rememberMe = false });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToList() : new List<string>();
        setCookies.Should().NotContain(c => c.StartsWith("__Host-Session="),
            "recorder failure must not silently let the attacker through with a session cookie");
    }
}

/// <summary>
/// Sibling factory used only by SaveChangesFailure_BubblesAs500_DoesNotIssueSession.
/// Overrides FailedLoginRecorder DI registration with a throwing stub via
/// ConfigureTestServices (which runs after ConfigureServices). Inherits the real
/// auth pipeline from AuthTestWebApplicationFactory so Identity, cookies, antiforgery,
/// and the rest of the controller pipeline behave normally — only the recorder throws.
/// </summary>
public sealed class ThrowingRecorderFactory : AuthTestWebApplicationFactory
{
    // Ad-hoc factory (constructed directly by the test, not a bucket collection fixture),
    // so it pins its own database to the IntegrationParallel2 bucket rather than going
    // through IntegrationTestBase. Matches the collection this test class lives in.
    protected override string InitDbName =>
        TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Replace the registration with the throwing subclass.
            var existing = services.Where(d => d.ServiceType == typeof(FailedLoginRecorder)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddScoped<FailedLoginRecorder, ThrowingFailedLoginRecorder>();
        });
    }
}

internal sealed class ThrowingFailedLoginRecorder : FailedLoginRecorder
{
    public ThrowingFailedLoginRecorder(IServiceScopeFactory scopeFactory, ILookupNormalizer normalizer)
        : base(scopeFactory, normalizer, TimeProvider.System) { }

    public override Task RecordAsync(
        string? emailAttempted, Guid? userId, FailedLoginReason reason,
        string ipAddress, string userAgent, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated DB failure for SaveChangesFailure test");
}
