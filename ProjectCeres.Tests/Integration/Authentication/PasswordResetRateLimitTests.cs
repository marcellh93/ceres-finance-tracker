using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Email;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class PasswordResetRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public PasswordResetRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => AuthTestTokenCleanup.DeleteAllTestTokensAsync(_factory);

    // ── Test #24 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Same IP, 10 reset requests with distinct emails → 10 succeed. 11th returns 429
    /// with Retry-After. Reuses the AuthLoginByIp policy (10/min/IP).
    /// </summary>
    [Fact]
    public async Task Request_per_ip_limit_returns_429_at_11th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request",
                new { email = $"distinct-{i}-{Guid.NewGuid():N}@example.com" });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request",
            new { email = $"distinct-11-{Guid.NewGuid():N}@example.com" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
    }

    // ── Test #25 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Same email, 5 requests succeed. 6th returns 429 (service-side MemoryCache gate,
    /// 5/hour/email). Uses a unique Guid email so the bucket starts empty regardless of
    /// prior test state.
    /// </summary>
    [Fact]
    public async Task Request_per_email_limit_returns_429_at_6th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        // Unique email per test invocation — bucket starts empty.
        var email = $"per-email-limit-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test #26 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Unknown email, 5 requests succeed. 6th returns 429 — the per-email bucket fires
    /// regardless of whether the user exists. Pins: no information leak about user existence.
    /// Uses a unique email per test to avoid cross-test bucket pollution.
    /// </summary>
    [Fact]
    public async Task Request_per_email_limit_does_not_leak_user_existence()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        // Unique never-registered email — bucket starts empty, user never exists.
        var unknownEmail = $"never-existed-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email = unknownEmail });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "unknown emails burn the per-email bucket the same way as known ones");
    }

    // ── Test #27 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Fills the per-email bucket (5 requests), then resets the MemoryCache to simulate
    /// an elapsed one-hour window. A 6th request after the reset must succeed.
    /// Uses WithFreshMemoryCache() so the bucket is isolated; Compact(1.0) evicts all
    /// entries to simulate expiry.
    /// </summary>
    [Fact]
    public async Task Request_per_email_bucket_resets_after_one_hour()
    {
        await using var factory = _factory
            .WithReplacedService<IEmailService>(new NoopEmailService())
            .WithWebHostBuilder(_ => { }); // produce an independent DI root
        var client = factory.CreateClient();

        var email = $"reset-bucket-{Guid.NewGuid():N}@example.com";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/request", new { email });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Simulate elapsed window: evict all MemoryCache entries (100% compact).
        RateLimitedAuthTestWebApplicationFactory.ResetMemoryCache(factory);

        var afterReset = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        afterReset.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #28 ────────────────────────────────────────────────────────────
    /// <summary>
    /// Confirm endpoint is also rate-limited by AuthLoginByIp (10/min/IP). 10 invalid-token
    /// confirms each return 401; the 11th returns 429.
    /// </summary>
    [Fact]
    public async Task Confirm_endpoint_per_ip_limit_returns_429_at_11th_attempt()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token = $"definitely-not-a-token-{i}", newPassword = "something fresh and long" });
            // Not 429 yet — expect 401 INVALID_RESET_TOKEN.
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token = "yet-another", newPassword = "something fresh and long" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test #33 ────────────────────────────────────────────────────────────
    /// <summary>
    /// MFA user: 10 probe calls to /confirm without totpCode each return 200 requiresTotp,
    /// burning 10/10 of the IP bucket. The 11th call returns 429.
    /// Pins: a probe that returns 200 (not 401/204) still counts against the rate limit.
    /// </summary>
    [Fact]
    public async Task Confirm_first_call_returning_requiresTotp_does_count_against_per_ip_rate_limit()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) => { captured.Add(m); return Task.CompletedTask; });

        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"probe-counts-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        // EnrollUserMfaAsync requires AuthTestWebApplicationFactory; use _factory directly.
        // The derived factory shares the same DB, so enrollment is visible to all.
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var reqResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        reqResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // The /request call above consumed 1 of the 10 IP slots. Fire 9 probe calls so
        // the total reaches 10/10. Each probe (no totpCode) returns 200 requiresTotp.
        for (var i = 0; i < 9; i++)
        {
            var probe = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token, newPassword = "fresh horse battery staple" });
            probe.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 11th total IP call returns 429 — pin: probe DOES count against the shared IP bucket.
        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
