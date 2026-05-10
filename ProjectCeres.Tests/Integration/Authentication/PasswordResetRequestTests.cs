using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class PasswordResetRequestTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetRequestTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task Request_with_known_email_issues_token_and_sends_email()
    {
        // Use a strict mock so any unanticipated call fails the test.
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        emailMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var client = factory.CreateClient();

        var email = $"req-known-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .SingleAsync(Timeout30s());

        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        token.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(16));
        token.ConsumedAt.Should().BeNull();
        token.MfaVerifiedAt.Should().BeNull();

        emailMock.Verify(e => e.SendAsync(
            It.Is<EmailMessage>(m => m.To == email && m.Subject.Contains("Reset")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Request_with_unknown_email_returns_204_with_same_timing()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        // Warm-up: discard the first measurement of each branch (JIT, cache fill).
        // Each known email is unique so the per-email rate limiter (5/hour) is never
        // tripped across the warmup + 5 measurement iterations.
        var warmupKnownEmail = $"req-known-timing-warmup-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, warmupKnownEmail);
        await Hit(factory, client, warmupKnownEmail);
        await Hit(factory, client, $"unknown-{Guid.NewGuid():N}@example.com");

        var knownTimings = new List<long>();
        var unknownTimings = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            // Use a fresh registered email for each iteration to stay under the 5/hour rate gate.
            var knownEmail = $"req-known-timing-{i}-{Guid.NewGuid():N}@example.com";
            await AuthTestFixture.RegisterUserAsync(factory, knownEmail);
            knownTimings.Add(await Measure(factory, client, knownEmail));
            unknownTimings.Add(await Measure(factory, client, $"unknown-{Guid.NewGuid():N}@example.com"));
        }

        var meanKnown = knownTimings.Average();
        var meanUnknown = unknownTimings.Average();
        var diffMs = Math.Abs(meanKnown - meanUnknown);

        diffMs.Should().BeLessThan(50,
            "constant-time defence requires |mean diff| < 50ms; got known={0}ms unknown={1}ms",
            meanKnown, meanUnknown);
    }

    private async Task<long> Measure(WebApplicationFactory<Program> factory, HttpClient client, string email)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await Hit(factory, client, email);
        sw.Stop();
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return sw.ElapsedMilliseconds;
    }

    private Task<HttpResponseMessage> Hit(WebApplicationFactory<Program> factory, HttpClient client, string email) =>
        AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });

    [Fact]
    public async Task Request_with_unknown_email_does_not_create_db_row()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        // Use a GUID-suffixed email that is guaranteed never registered in this run.
        var unknownEmail = $"never-registered-{Guid.NewGuid():N}@example.com";

        // Pre-condition: no user with this email exists, so no token can exist for them.
        // Count tokens before the request so we can assert the count does not increase.
        int tokenCountBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tokenCountBefore = await db.PasswordResetTokens.CountAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenCountAfter = await db.PasswordResetTokens.CountAsync(Timeout30s());
            tokenCountAfter.Should().Be(tokenCountBefore,
                "a request for an unknown email must not create any PasswordResetToken row");
        }
    }

    [Fact]
    public async Task Request_with_unknown_email_does_not_send_email()
    {
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        // No Setup — strict mock fails on any call.

        await using var factory = _factory.WithReplacedService<IEmailService>(emailMock.Object);
        var client = factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        emailMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Request_does_not_log_user_email_or_password_to_default_logger()
    {
        var capturedLogs = new List<string>();
        await using var factory = _factory.WithCapturedLogger(capturedLogs)
                                          .WithWebHostBuilder(builder =>
                                              builder.ConfigureTestServices(services =>
                                              {
                                                  services.RemoveAll<IEmailService>();
                                                  services.AddSingleton<IEmailService>(new NoopEmailService());
                                              }));
        var client = factory.CreateClient();

        var email = $"pii-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The LogOnlyEmailService DOES log the body, but only at Information when active.
        // The default logger (production) is the one we assert against. With NoopEmailService
        // the email body never reaches a logger at all.
        capturedLogs.Should().NotContain(s => s.Contains(email),
            "no production code path should log the user's email");
    }

    [Fact]
    public async Task Request_is_subject_to_user_blocked_ip_middleware()
    {
        // Register a user, then add a blocked-IP entry pointing at the test client's loopback IP.
        // Subsequent /password-reset/request from that IP must be 403 (UserBlockedIpMiddleware fires
        // before the controller).
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var email = $"blocked-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // TestServer's RemoteIpAddress is null, so the controller produces ip = "".
            // Block that same empty-string IP to match what the test client sends.
            db.UserBlockedIps.Add(new UserBlockedIp
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                IpAddress = "",
                Reason = "test",
                BlockedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Request_for_unknown_email_runs_argon2_dummy_hash()
    {
        // Verify the timing path: the unknown branch calls RunDummyHash. We assert by
        // measuring elapsed time — RunDummyHash takes ~100ms by Argon2id design at the
        // pinned m=19456,t=2,p=1 parameters, so a request that omits it would land
        // closer to single-digit ms.
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50,
            "Argon2id dummy hash should dominate wall-clock time on the unknown branch");
    }
}
