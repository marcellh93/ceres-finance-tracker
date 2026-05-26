using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
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
public class LockoutUnlockIssuanceTests : IAsyncLifetime
{
    private const string EmailDomain = "@lockout-issue.local";

    private readonly AuthTestWebApplicationFactory _factory;

    public LockoutUnlockIssuanceTests(AuthTestWebApplicationFactory factory) => _factory = factory;

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
            await db.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(r => r.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
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

    private static async Task DriveLockoutAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        AuthTestWebApplicationFactory baseFactory,
        HttpClient client, string email)
    {
        for (int i = 0; i < 10; i++)
        {
            await AuthTestFixture.PostJsonWithCsrfAsync(baseFactory, client,
                "/api/auth/login",
                new { email, password = $"wrong-pwd-{i}-but-long-enough", rememberMe = false });
        }
    }

    [Fact]
    public async Task Login_transitioning_into_lockout_writes_token_row_AND_queues_email()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var email = $"transition-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        var client = factory.CreateClient();

        await DriveLockoutAsync(factory, _factory, client, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokens = await db.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ToListAsync();
            tokens.Should().ContainSingle();
            tokens[0].ConsumedAt.Should().BeNull();
            tokens[0].ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        var unlockEmails = captured.Where(m => m.To.Address == email).ToList();
        unlockEmails.Should().ContainSingle();
        unlockEmails[0].Subject.Should().Contain("locked");
        unlockEmails[0].BodyText.Should().Contain("/account/unlock#token=");
    }

    [Fact]
    public async Task Login_already_locked_does_NOT_issue_a_second_token()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var email = $"already-locked-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Manually lock the user without going through the failure loop, so we start
        // already-locked with zero tokens issued.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(user.Id.ToString());
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddMinutes(15));
        }

        captured.Clear();
        var client = factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = "any-wrong-but-long-enough-pwd", rememberMe = false });

        using var scope2 = factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.LockoutUnlockTokens.IgnoreQueryFilters().AnyAsync(t => t.UserId == user.Id)).Should().BeFalse(
            "no transition occurred (account was already locked), so no token should be issued");
        captured.Should().NotContain(m => m.To.Address == email,
            "no transition → no email");
    }

    [Fact]
    public async Task LoginTotp_observing_locked_state_does_NOT_issue_token()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var email = $"totp-observe-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // First, drive the account into lockout via the password path (engages exactly one transition).
        var client = factory.CreateClient();
        await DriveLockoutAsync(factory, _factory, client, email);

        // Now count tokens — should be one from the password-path transition.
        int tokenCountAfterPasswordPath;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tokenCountAfterPasswordPath = await db.LockoutUnlockTokens
                .IgnoreQueryFilters().Where(t => t.UserId == user.Id).CountAsync();
        }
        tokenCountAfterPasswordPath.Should().Be(1);

        // Hit /login/totp with bad TOTP on the locked account — must NOT issue another token.
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = "000000" });

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var totalTokens = await db2.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).CountAsync();
        totalTokens.Should().Be(1, "LoginTotp's IsLockedOutAsync branches observe the state but do not transition it");
    }

    [Fact]
    public async Task Issue_supersedes_prior_unconsumed_tokens()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var email = $"supersede-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Pre-insert a stale unconsumed token row.
        Guid staleId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();
            staleId = Guid.NewGuid();
            db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = staleId,
                UserId = user.Id,
                // Stage 9.1.5.a — TokenLookup is NOT NULL + unique. This stale row has
                // no corresponding raw token (the test fabricates the hash from a
                // fixed string), so a random 32-byte value satisfies the schema
                // contract; the supersede test never reads TokenLookup back.
                TokenLookup = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                TokenHash = generator.Hash("stale-raw-token-value-doesnt-matter"),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                ConsumedAt = null,
            });
            await db.SaveChangesAsync();
        }

        // Drive a fresh lockout transition.
        var client = factory.CreateClient();
        await DriveLockoutAsync(factory, _factory, client, email);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var stale = await db2.LockoutUnlockTokens.IgnoreQueryFilters().SingleAsync(t => t.Id == staleId);
        stale.ConsumedAt.Should().NotBeNull("the stale token must be superseded on issue");

        var unconsumed = await db2.LockoutUnlockTokens
            .IgnoreQueryFilters().Where(t => t.UserId == user.Id && t.ConsumedAt == null).ToListAsync();
        unconsumed.Should().ContainSingle("exactly one fresh unconsumed row should exist after supersession");
    }

    [Fact]
    public async Task Issue_email_send_failure_does_not_roll_back_token_row()
    {
        // Replace IEmailService with a throwing mock; the issue still commits a token row,
        // and the user-facing login response is still ACCOUNT_LOCKED_OUT (not 500).
        var throwingMock = new Mock<IEmailService>(MockBehavior.Strict);
        throwingMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("simulated SMTP failure"));

        await using var factory = _factory.WithReplacedService<IEmailService>(throwingMock.Object);
        var email = $"email-fail-{Guid.NewGuid():N}{EmailDomain}";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        var client = factory.CreateClient();

        await DriveLockoutAsync(factory, _factory, client, email);

        // 11th attempt sees ACCOUNT_LOCKED_OUT (not 500) — email failure must not break the flow.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = "still-wrong-pwd-long-enough", rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.LockoutUnlockTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ToListAsync();
        tokens.Should().ContainSingle("token row must commit even when the email send throws");
    }

    [Fact]
    public async Task Issue_DB_write_failure_does_not_break_lockout_response()
    {
        // Exercises AuthController.Login's outer try/catch around IssueAsync. IssueAsync's
        // own catch only handles email failures; this one covers a non-email throw (e.g.
        // DB write failure). The lockout response must still surface ACCOUNT_LOCKED_OUT,
        // not 500, because the user-visible outcome (account is locked) is independent of
        // whether the unlock-token side-effect succeeded.
        await using var factory = new ThrowingLockoutUnlockServiceFactory();
        var email = $"issue-throws-{Guid.NewGuid():N}{EmailDomain}";
        await AuthTestFixture.RegisterUserAsync(factory, email);
        var client = factory.CreateClient();

        await DriveLockoutAsync(factory, _factory, client, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = "still-wrong-pwd-long-enough", rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "IssueAsync throw must not propagate to a 500 — the outer catch must convert it to the standard ACCOUNT_LOCKED_OUT response");
    }
}

/// <summary>
/// Sibling factory used only by Issue_DB_write_failure_does_not_break_lockout_response.
/// Overrides LockoutUnlockService with a subclass whose IssueAsync throws a non-email
/// exception, exercising AuthController.Login's outer try/catch.
/// </summary>
public sealed class ThrowingLockoutUnlockServiceFactory : AuthTestWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<LockoutUnlockService>();
            services.AddScoped<LockoutUnlockService, ThrowingLockoutUnlockService>();
        });
    }
}

internal sealed class ThrowingLockoutUnlockService : LockoutUnlockService
{
    public ThrowingLockoutUnlockService(
        Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
        AppDbContext db,
        Argon2idPasswordHasher argon,
        LockoutUnlockTokenGenerator tokens,
        TokenLookupHasher lookupHasher,
        IEmailService email,
        ProjectCeres.Common.Email.IEmailComposer composer,
        ProjectCeres.Common.Email.IEmailRecipientResolver recipients,
        ProjectCeres.Common.Email.ILanguageResolver languages,
        Microsoft.Extensions.Logging.ILogger<LockoutUnlockService> logger,
        IAuditLogWriter auditLog,
        LockoutCache lockoutCache)
        : base(userManager, db, argon, tokens, lookupHasher, email, composer, recipients, languages, logger, auditLog, lockoutCache, TimeProvider.System) { }

    public override Task IssueAsync(
        Guid userId, string userEmail, string ip, string userAgent,
        string unlockUrlBase, CancellationToken ct)
        => throw new InvalidOperationException("simulated DB-write failure inside IssueAsync");
}
