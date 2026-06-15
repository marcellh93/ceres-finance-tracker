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

[Collection("IntegrationTests")]
public class PasswordResetRequestTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetRequestTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // ── Test #1 ─────────────────────────────────────────────────────────────
    // Happy path: known email → 204, token row written, email sent once.
    [Fact]
    public async Task Request_with_known_email_issues_token_and_sends_email()
    {
        // Use a strict mock so any unanticipated call fails the test.
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        emailMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        await using var factory = _factory.WithReplacedService(emailMock.Object);
        var client = factory.CreateClient();

        var email = $"req-known-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id)
            .SingleAsync(Timeout30s());

        token.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        token.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(16));
        token.ConsumedAt.Should().BeNull();
        token.MfaVerifiedAt.Should().BeNull();
        // Stage 6.15 — RequestAsync must populate the HMAC-derived TokenLookup so the
        // unique index is satisfied AND /confirm can locate the row in O(1).
        token.TokenLookup.Should().NotBeNull().And.HaveCount(32);

        emailMock.Verify(e => e.SendAsync(
            It.Is<EmailMessage>(m => m.To.Address == email && m.Subject.Contains("Reset")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Test #7 ─────────────────────────────────────────────────────────────
    // Constant-time defence (post-2026-05-12): deterministic Argon2id-call-count
    // assertion. Pre-2026-05-12 this was a wall-clock median test with a tuned
    // threshold that kept flaking under integration-suite CPU contention; the
    // wall-clock approach cannot stably pin the property under that load because
    // the variance floor is the same order of magnitude as a single Argon2id call.
    // The count-based assertion is exact: by the time the HTTP response returns,
    // every Argon2id call is complete (Argon2id is synchronous within the request
    // handler), so the counter holds an exact integer total. Two branches with
    // equal counts perform equal Argon2id work — which is the security property
    // the constant-time defence claims to guarantee.
    [Fact]
    public async Task Request_with_unknown_email_performs_same_Argon2id_count_as_known_branch()
    {
        await using var factory = _factory.WithReplacedServiceAndArgon2idCounter<IEmailService>(
            new NoopEmailService(), out var counter);
        var client = factory.CreateClient();

        // Warm up JIT for the counting hasher path; counter values from this run
        // are discarded.
        var warmupKnownEmail = $"req-known-warm-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, warmupKnownEmail);
        await Hit(factory, client, warmupKnownEmail);
        await Hit(factory, client, $"unknown-warm-{Guid.NewGuid():N}@example.com");

        // Known-email branch: register a fresh user, count Argon2id calls during
        // the password-reset/request handler.
        var knownEmail = $"req-known-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(factory, knownEmail);
        counter.Reset();
        var knownResp = await Hit(factory, client, knownEmail);
        knownResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var knownCount = counter.Count;

        // Unknown-email branch.
        counter.Reset();
        var unknownResp = await Hit(factory, client, $"unknown-{Guid.NewGuid():N}@example.com");
        unknownResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var unknownCount = counter.Count;

        unknownCount.Should().Be(knownCount,
            "constant-time defence: both branches must perform the same number of " +
            "Argon2id operations. known branch performed {0}; unknown branch performed {1}. " +
            "Stage 6.16 closed a gap where the unknown branch was missing the second " +
            "RunDummyHash mirroring `_tokens.Hash(rawToken)` on the known branch. If " +
            "this trips, an Argon2id call on one branch was added or removed without " +
            "the mirror on the other.",
            knownCount, unknownCount);

        // Sanity: both branches must have actually run Argon2id. A regression that
        // removes Argon2id from BOTH branches would otherwise pass count-equality
        // trivially.
        knownCount.Should().BeGreaterThan(0,
            "the known branch must perform at least one Argon2id (verify + token-hash)");
    }

    private Task<HttpResponseMessage> Hit(WebApplicationFactory<Program> factory, HttpClient client, string email) =>
        AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/password-reset/request", new { email });

    // ── Test #8 ─────────────────────────────────────────────────────────────
    // Unknown email → no PasswordResetToken row written to DB.
    [Fact]
    public async Task Request_with_unknown_email_does_not_create_db_row()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var unknownEmail = $"never-registered-{Guid.NewGuid():N}@example.com";

        int tokenCountBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tokenCountBefore = await db.PasswordResetTokens.IgnoreQueryFilters().CountAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenCountAfter = await db.PasswordResetTokens.IgnoreQueryFilters().CountAsync(Timeout30s());
            tokenCountAfter.Should().Be(tokenCountBefore,
                "a request for an unknown email must not create any PasswordResetToken row");
        }
    }

    // ── Test #9 ─────────────────────────────────────────────────────────────
    // Unknown email → IEmailService.SendAsync never called (strict mock, no setup).
    [Fact]
    public async Task Request_with_unknown_email_does_not_send_email()
    {
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        // No Setup — strict mock fails on any call.

        await using var factory = _factory.WithReplacedService(emailMock.Object);
        var client = factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        emailMock.VerifyNoOtherCalls();
    }

    // ── Test #14 ────────────────────────────────────────────────────────────
    // PII redaction: the user's email address must not appear in any log message
    // written by production code during the request.
    [Fact]
    public async Task Request_does_not_log_user_email_or_password_to_default_logger()
    {
        var capturedLogs = new List<string>();
        // Chain WithCapturedLogger (base factory) then inline-replace IEmailService.
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

        // With NoopEmailService the email body never reaches a logger.
        // Any production logger that emits the email address is a PII leak.
        capturedLogs.Should().NotContain(s => s.Contains(email),
            "no production code path should log the user's email address");
    }

    // ── Test #16 ────────────────────────────────────────────────────────────
    // TODO(Stage 6c-followup): UserBlockedIpMiddleware currently only fires on
    // authenticated requests (it guards the IsAuthenticated path). The anonymous
    // /password-reset/request endpoint carries an email that could be used to
    // enumerate blocked users' addresses, so it should also be guarded.
    // Tracked separately — not shipped in Stage 6c.1.

    // ── Test #46 ────────────────────────────────────────────────────────────
    // Verify the unknown branch actually executes the Argon2id dummy hash: the
    // request wall-clock time must exceed 50ms (Argon2id at the pinned parameters
    // m=19456,t=2,p=1 takes ~100ms; a no-op path would be single-digit ms).
    [Fact]
    public async Task Request_for_unknown_email_runs_argon2_dummy_hash()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50,
            "Argon2id dummy hash should dominate wall-clock time on the unknown branch; " +
            "elapsed was {0}ms — if this is < 50ms RunDummyHash was likely removed",
            sw.ElapsedMilliseconds);
    }
}
