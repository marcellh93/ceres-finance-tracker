using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class PasswordResetSessionRevocationTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetSessionRevocationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // ── Test #35 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Successful_reset_revokes_all_user_sessions()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"sessrev-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Plant 3 active sessions (mix of persistent and ephemeral).
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.UserSessions.Add(new UserSession
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    IpCreatedAt = $"127.0.0.{i + 1}",
                    UserAgent = "test",
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    IsPersistent = i == 2,
                });
            }
            await db.SaveChangesAsync(Timeout30s());
        }

        // Request + confirm password reset.
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // All 3 sessions must be revoked.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await db.UserSessions
                .Where(s => s.UserId == user.Id)
                .ToListAsync(Timeout30s());
            sessions.Should().HaveCount(3);
            sessions.Should().OnlyContain(s => s.RevokedAt != null);
        }
    }

    // ── Test #36 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Successful_reset_invalidates_in_flight_session_cookie_via_security_stamp()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);

        var email = $"stamp-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        // Capture stamp before reset.
        string oldStamp;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            oldStamp = await um.GetSecurityStampAsync(fresh!);
        }

        // Request + confirm password reset.
        var client = factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Stamp must have changed — this is the mechanism SecurityStampValidator uses to
        // invalidate in-flight cookies without needing a distributed session store.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            var newStamp = await um.GetSecurityStampAsync(fresh!);
            newStamp.Should().NotBe(oldStamp,
                "SecurityStamp regen on reset is what invalidates in-flight cookies via SecurityStampValidator");
        }
    }
}
