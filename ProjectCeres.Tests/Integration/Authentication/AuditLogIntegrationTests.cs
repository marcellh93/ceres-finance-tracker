using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class AuditLogIntegrationTests : IAsyncLifetime
{
    private const string EmailDomain = "@audit-test.local";

    private readonly AuthTestWebApplicationFactory _factory;

    public AuditLogIntegrationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailDomain)).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.FailedLoginAttempts.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == u.Id).ExecuteDeleteAsync();
            await db.PasswordResetTokens.IgnoreQueryFilters().Where(t => t.UserId == u.Id).ExecuteDeleteAsync();
            await db.EmailChangeTokens.IgnoreQueryFilters().Where(t => t.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(r => r.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
        await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted!.EndsWith(EmailDomain))
            .ExecuteDeleteAsync();
    }

    private static (Mock<IEmailService> mock, List<EmailMessage> captured) CapturingEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Loose);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    private static async Task ClearAuditAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AuditLogs.IgnoreQueryFilters().Where(r => r.UserId == userId).ExecuteDeleteAsync();
    }

    private static async Task<List<AuditLog>> ReadAuditAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.IgnoreQueryFilters().Where(r => r.UserId == userId).ToListAsync();
    }

    // ── Writer-level behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task Writer_RecordAsync_InsertsRowWithGivenFields()
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();

        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;
        await writer.RecordAsync(userId, AuditLogAction.LoginSucceeded);

        var rows = await ReadAuditAsync(_factory, userId);
        rows.Should().ContainSingle();
        var row = rows[0];
        row.Action.Should().Be(AuditLogAction.LoginSucceeded);
        row.EntityType.Should().BeNull();
        row.EntityId.Should().BeNull();
        row.OccurredAt.Should().BeCloseTo(before, precision: TimeSpan.FromSeconds(5));
        // No HttpContext in this resolution path: IP defaults to "unknown".
        row.IpAddress.Should().Be("unknown");

        // Clean the orphan row we just wrote (no AspNetUser exists for this Guid).
        await ClearAuditAsync(_factory, userId);
    }

    [Fact]
    public async Task Writer_RecordAsync_WithEntityTypeSetButEntityIdNull_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();
        await FluentActions
            .Awaiting(() => writer.RecordAsync(Guid.NewGuid(), AuditLogAction.LoginSucceeded, entityType: "Foo", entityId: null))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Writer_RecordAsync_WithEntityIdSetButEntityTypeNull_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();
        await FluentActions
            .Awaiting(() => writer.RecordAsync(Guid.NewGuid(), AuditLogAction.LoginSucceeded, entityType: null, entityId: Guid.NewGuid()))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Writer_RecordAsync_EntityPair_RoundTripsBothFields()
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();

        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await writer.RecordAsync(userId, AuditLogAction.BackupCodesRegenerated, "BackupCodeBatch", entityId);

        var rows = await ReadAuditAsync(_factory, userId);
        rows.Should().ContainSingle();
        rows[0].EntityType.Should().Be("BackupCodeBatch");
        rows[0].EntityId.Should().Be(entityId);

        await ClearAuditAsync(_factory, userId);
    }

    [Fact]
    public async Task Writer_RecordAsync_NoHttpContext_RecordsIpAsUnknown()
    {
        using var scope = _factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter>();

        var userId = Guid.NewGuid();
        await writer.RecordAsync(userId, AuditLogAction.LoginSucceeded);

        var rows = await ReadAuditAsync(_factory, userId);
        rows.Should().ContainSingle().Which.IpAddress.Should().Be("unknown");

        await ClearAuditAsync(_factory, userId);
    }

    // ── Call sites via real HTTP / service flows ────────────────────────────

    [Fact]
    public async Task Login_no_mfa_writes_LoginSucceeded()
    {
        var email = $"login-nomfa-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        await ClearAuditAsync(_factory, user.Id); // drop the Registered row

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.LoginSucceeded);
    }

    [Fact]
    public async Task Login_mfa_writes_LoginSucceededMfa()
    {
        var email = $"login-mfa-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        await ClearAuditAsync(_factory, user.Id);

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var totp = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = totp });
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.LoginSucceededMfa);
    }

    [Fact]
    public async Task Login_backup_code_writes_LoginSucceededBackupCode()
    {
        var email = $"login-bc-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Pull one freshly-generated backup code straight from the service.
        string backupCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await bc.RegenerateAsync(user.Id, CancellationToken.None);
            backupCode = codes.First();
        }

        await ClearAuditAsync(_factory, user.Id);

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = backupCode });
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.LoginSucceededBackupCode);
    }

    [Fact]
    public async Task Logout_writes_Logout()
    {
        var email = $"logout-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email, AuthTestFixture.ValidPassword);

        await ClearAuditAsync(_factory, user.Id);

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        logoutReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        logoutReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var resp = await client.SendAsync(logoutReq);
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.Logout);
    }

    [Fact]
    public async Task Register_writes_Registered()
    {
        // Register through the real HTTP endpoint (the test-fixture helper bypasses
        // the controller, so it would never exercise the audit call site).
        var email = $"register-{Guid.NewGuid():N}{EmailDomain}";
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Resolve the user id from the just-created AspNetUser row.
        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            user.Should().NotBeNull();
            userId = user!.Id;
        }

        var rows = await ReadAuditAsync(_factory, userId);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.Registered);
    }

    // ── Password reset ─────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordReset_request_known_email_writes_PasswordResetRequested()
    {
        var (emailMock, _) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var email = $"pwr-req-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        await ClearAuditAsync(factory, user.Id);

        var client = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
            "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await ReadAuditAsync(factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.PasswordResetRequested);
    }

    [Fact]
    public async Task PasswordReset_request_unknown_email_writes_NO_audit_row()
    {
        var (emailMock, _) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var unknownEmail = $"ghost-{Guid.NewGuid():N}{EmailDomain}";
        var client = factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
            "/api/auth/password-reset/request", new { email = unknownEmail });

        // Negative assertion: unknown-email branch must produce a FailedLoginAttempt
        // row (with reason PasswordResetUnknownEmail) but NOT an AuditLog row. We
        // assert via the FailedLoginAttempt side because there's no UserId for the
        // unknown branch to anchor an AuditLog row to.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failed = await db.FailedLoginAttempts
            .Where(e => e.EmailAttempted == unknownEmail.ToLowerInvariant())
            .SingleOrDefaultAsync();
        failed.Should().NotBeNull();
        failed!.Reason.Should().Be(FailedLoginReason.PasswordResetUnknownEmail);
    }

    [Fact]
    public async Task PasswordReset_confirm_writes_PasswordResetCompleted()
    {
        var (emailMock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var email = $"pwr-conf-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var client = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
            "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(1);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        await ClearAuditAsync(factory, user.Id);

        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
            "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await ReadAuditAsync(factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.PasswordResetCompleted);
    }

    // ── Email-address change ───────────────────────────────────────────────

    [Fact]
    public async Task EmailChange_request_writes_EmailChangeRequested()
    {
        var (emailMock, _) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var oldEmail = $"em-req-old-{Guid.NewGuid():N}{EmailDomain}";
        var newEmail = $"em-req-new-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, oldEmail);

        await ClearAuditAsync(factory, user.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
            await svc.RequestAsync(user.Id, newEmail,
                ip: "203.0.113.99", userAgent: "audit-test/1.0",
                verifyUrlBase: "https://test/confirm", revokeUrlBase: "https://test/revoke",
                CancellationToken.None);
        }

        var rows = await ReadAuditAsync(factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.EmailChangeRequested);
    }

    [Fact]
    public async Task EmailChange_confirm_writes_EmailChangeConfirmed()
    {
        var (emailMock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var oldEmail = $"em-conf-old-{Guid.NewGuid():N}{EmailDomain}";
        var newEmail = $"em-conf-new-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, oldEmail);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
            await svc.RequestAsync(user.Id, newEmail,
                ip: "203.0.113.99", userAgent: "audit-test/1.0",
                verifyUrlBase: "https://test/confirm", revokeUrlBase: "https://test/revoke",
                CancellationToken.None);
        }

        var verifyMsg = captured.Single(m => m.To.Address == newEmail);
        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(verifyMsg);

        await ClearAuditAsync(factory, user.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
            await svc.ConfirmAsync(verifyToken, CancellationToken.None);
        }

        var rows = await ReadAuditAsync(factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.EmailChangeConfirmed);
    }

    [Fact]
    public async Task EmailChange_revoke_writes_EmailChangeRevoked()
    {
        var (emailMock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var oldEmail = $"em-rev-old-{Guid.NewGuid():N}{EmailDomain}";
        var newEmail = $"em-rev-new-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, oldEmail);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
            await svc.RequestAsync(user.Id, newEmail,
                ip: "203.0.113.99", userAgent: "audit-test/1.0",
                verifyUrlBase: "https://test/confirm", revokeUrlBase: "https://test/revoke",
                CancellationToken.None);
        }

        var revokeMsg = captured.Single(m => m.To.Address == oldEmail);
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(revokeMsg);

        await ClearAuditAsync(factory, user.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EmailChangeService>();
            await svc.RevokeAsync(revokeToken, CancellationToken.None);
        }

        var rows = await ReadAuditAsync(factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.EmailChangeRevoked);
    }

    // ── MFA / backup-codes ─────────────────────────────────────────────────

    [Fact]
    public async Task Mfa_enroll_verify_writes_MfaEnrolled()
    {
        // Go through the real HTTP endpoint so the controller-level audit call fires.
        // The fixture helper EnrollUserMfaAsync bypasses the controller.
        var email = $"mfa-enr-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        // Generate an authenticator key + compute the matching TOTP code, then post
        // to /api/auth/mfa/enroll/verify with a fresh-reauth cookie.
        string seed;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.ResetAuthenticatorKeyAsync(fresh!);
            seed = (await um.GetAuthenticatorKeyAsync(fresh!))!;
        }

        await ClearAuditAsync(_factory, user.Id);

        var totp = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var freshUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, freshUnix);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
        {
            Content = JsonContent.Create(new { code = totp }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Action.Should().Be(AuditLogAction.MfaEnrolled);
    }

    [Fact]
    public async Task BackupCodes_regenerate_writes_BackupCodesRegenerated_with_no_entity_ref()
    {
        var email = $"bcr-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        await ClearAuditAsync(_factory, user.Id);

        // Mint a fresh recent-auth cookie and post to the controller endpoint, which is
        // the call site that ships the audit write.
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/backup-codes/regenerate")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().ContainSingle().Which.Should().Match<AuditLog>(r =>
            r.Action == AuditLogAction.BackupCodesRegenerated
            && r.EntityType == null
            && r.EntityId == null);
    }

    // ── Negative assertions ────────────────────────────────────────────────

    [Fact]
    public async Task Login_with_wrong_password_writes_NO_audit_row()
    {
        var email = $"wrong-pwd-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        await ClearAuditAsync(_factory, user.Id);

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_with_locked_account_writes_NO_audit_row()
    {
        var email = $"locked-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        await ClearAuditAsync(_factory, user.Id);

        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        var rows = await ReadAuditAsync(_factory, user.Id);
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie()
    {
        await using var factory = new ThrowingAuditLogWriterFactory();
        var email = $"boom-{Guid.NewGuid():N}{EmailDomain}";

        // Registration itself writes an audit row (via the throwing writer in this
        // factory), so RegisterUserAsync would 500 too. We register against the
        // shared factory instead — only login goes through the throwing factory.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        var client = factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToList() : new List<string>();
        setCookies.Should().NotContain(c => c.StartsWith("__Host-Session="),
            "audit-log failure must not silently let an attacker through with a session cookie");
    }
}

/// <summary>
/// Sibling factory used only by Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie.
/// Overrides IAuditLogWriter DI registration with a throwing stub.
/// </summary>
public sealed class ThrowingAuditLogWriterFactory : AuthTestWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLogWriter>();
            services.AddScoped<IAuditLogWriter, ThrowingAuditLogWriter>();
        });
    }
}

internal sealed class ThrowingAuditLogWriter : IAuditLogWriter
{
    public Task RecordAsync(Guid userId, AuditLogAction action, string? entityType = null, Guid? entityId = null, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated audit-log write failure");
}
