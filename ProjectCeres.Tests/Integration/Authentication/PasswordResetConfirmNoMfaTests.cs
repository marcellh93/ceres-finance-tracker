using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel3")]
public class PasswordResetConfirmNoMfaTests : IntegrationTestBase<Bucket3AuthFactory>
{
    private readonly Bucket3AuthFactory _factory;

    public PasswordResetConfirmNoMfaTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private (Mock<IEmailService> mock, List<EmailMessage> captured) StrictEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    private async Task<(string email, string token)> RequestResetAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        HttpClient client,
        List<EmailMessage> captured,
        string? email = null)
    {
        email ??= $"reset-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(1);
        return (email, AuthTestFixture.ExtractResetTokenFromMessage(captured[0]));
    }

    // ── Test #2 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_no_mfa_succeeds()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 1) Password actually changed: old password fails, new password succeeds.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(user!, AuthTestFixture.ValidPassword)).Should().BeFalse();
            (await userManager.CheckPasswordAsync(user!, "fresh horse battery staple")).Should().BeTrue();
        }

        // 2) Token consumed.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user!.Id)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().NotBeNull();
        }

        // 3) Notification email queued (request email + notification email = 2 total).
        captured.Should().HaveCount(2);
        captured[1].Subject.Should().Contain("password was changed");
    }

    // ── Test #4 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_clears_lockout()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Lock the account by hand.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            user.Should().NotBeNull();
            for (var i = 0; i < 10; i++) await um.AccessFailedAsync(user!);
            (await um.IsLockedOutAsync(user!)).Should().BeTrue();
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            (await um.IsLockedOutAsync(user!)).Should().BeFalse();
            (await um.GetAccessFailedCountAsync(user!)).Should().Be(0);
        }
    }

    // ── Test #5 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_no_mfa_promotes_EmailConfirmed_when_previously_false()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var email = $"unconfirmed-{Guid.NewGuid():N}@example.com";
        // Register without confirming the email — bypass AuthTestFixture which auto-confirms.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            (await um.CreateAsync(user, AuthTestFixture.ValidPassword)).Succeeded.Should().BeTrue();
            // Do NOT call ConfirmEmailAsync. user.EmailConfirmed = false at this point.
        }

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            user!.EmailConfirmed.Should().BeTrue();
        }
    }

    // ── Test #6 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_succeeds_when_account_is_currently_locked()
    {
        // Specifically asserts the success path is not gated on lockout —
        // the request itself should not be blocked.
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #10 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_does_not_consume_token_on_password_policy_failure()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Submit a too-short password. Policy violation, token NOT consumed.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "short" });
        resp1.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user!.Id)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull("token must remain usable after a policy rejection");
        }

        // Retry with a valid password — must succeed.
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #15 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_does_not_log_new_password_anywhere()
    {
        var capturedLogs = new List<string>();
        var captured = new List<EmailMessage>();
        // Chain WithCapturedLogger (returns base WebApplicationFactory<Program>) then
        // inline-replace IEmailService via WithWebHostBuilder — mirrors the Task 12 pattern.
        await using var factory = _factory.WithCapturedLogger(capturedLogs)
                                          .WithWebHostBuilder(builder =>
                                              builder.ConfigureTestServices(services =>
                                              {
                                                  services.RemoveAll<IEmailService>();
                                                  var strictMock = new Mock<IEmailService>(MockBehavior.Strict);
                                                  strictMock.Setup(e => e.SendAsync(
                                                          It.IsAny<EmailMessage>(),
                                                          It.IsAny<CancellationToken>()))
                                                      .Returns<EmailMessage, CancellationToken>((m, _) =>
                                                      {
                                                          captured.Add(m);
                                                          return Task.CompletedTask;
                                                      });
                                                  services.AddSingleton(strictMock.Object);
                                              }));
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        var sentinelPassword = $"sentinel-{Guid.NewGuid():N}-passw0rd";
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = sentinelPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        capturedLogs.Should().NotContain(s => s.Contains(sentinelPassword),
            "new password must not appear anywhere in logs");
    }

    // ── Test #17 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_with_no_mfa_user_ignores_extraneous_totpCode()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, token) = await RequestResetAsync(factory, client, captured);

        // No-MFA user submits a totpCode. Service must ignore it and proceed.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #18 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Token_replay_after_success_returns_401()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, token) = await RequestResetAsync(factory, client, captured);

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "another fresh horse" });
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp2.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_RESET_TOKEN");
    }

    // ── Test #21 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Expired_token_returns_401()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Force-expire the row in DB.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == user!.Id)
                .SingleAsync(Timeout30s());
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Test #20 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task New_request_supersedes_prior_unused_token()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token1) = await RequestResetAsync(factory, client, captured);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(2);
        var token2 = AuthTestFixture.ExtractResetTokenFromMessage(captured[1]);
        token2.Should().NotBe(token1);

        // Old token rejected.
        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = token1, newPassword = "fresh horse battery staple" });
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // New token accepted.
        var accepted = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = token2, newPassword = "fresh horse battery staple" });
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #39 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Failed_confirm_does_not_revoke_any_sessions()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, _) = await RequestResetAsync(factory, client, captured);

        // Plant 3 active sessions for the user.
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await um.FindByEmailAsync(email))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.UserSessions.Add(new UserSession
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    IpCreatedAt = "127.0.0.1",
                    UserAgent = "test",
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsPersistent = false,
                });
            }
            await db.SaveChangesAsync(Timeout30s());
        }

        // Submit an invalid token.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "not-a-real-token", newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // None of the 3 sessions should have been revoked.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var revoked = await db.UserSessions.IgnoreQueryFilters().CountAsync(
                s => s.UserId == userId && s.RevokedAt != null, Timeout30s());
            revoked.Should().Be(0);
        }
    }

    // ── Test #38 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Successful_reset_revokes_persistent_remember_me_cookie_session()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, token) = await RequestResetAsync(factory, client, captured);

        // Plant a persistent (remember-me) session.
        Guid persistentSessionId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var userId = (await um.FindByEmailAsync(email))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            persistentSessionId = Guid.NewGuid();
            db.UserSessions.Add(new UserSession
            {
                Id = persistentSessionId,
                UserId = userId,
                IpCreatedAt = "127.0.0.1",
                UserAgent = "test",
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsPersistent = true,
                PersistentTokenHash = "fake-hash-for-test",
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.IgnoreQueryFilters().FirstAsync(
                s => s.Id == persistentSessionId, Timeout30s());
            session.RevokedAt.Should().NotBeNull("persistent session must be revoked on reset");
        }
    }

    // ── Test #45 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_with_unknown_token_runs_at_least_one_argon2_verify()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", newPassword = "fresh horse battery staple" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50,
            "Argon2id verify should dominate wall-clock time on the unknown-token branch; " +
            "elapsed was {0}ms — if this is < 50ms RunDummyHash was likely removed",
            sw.ElapsedMilliseconds);
    }
}
