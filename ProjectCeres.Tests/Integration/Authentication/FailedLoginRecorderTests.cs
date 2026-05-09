using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class FailedLoginRecorderTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public FailedLoginRecorderTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@recorder-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
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
        var longEmail = new string('a', 1000) + "@recorder-test.local";
        using var scope = _factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<FailedLoginRecorder>();

        await recorder.RecordAsync(longEmail, null, FailedLoginReason.UnknownUser,
            "1.2.3.4", "ua", CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FailedLoginAttempts
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
}
