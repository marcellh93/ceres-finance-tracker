using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("RateLimitTests")]
public sealed class EmailRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public EmailRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    // ── Test 1 ──────────────────────────────────────────────────────────────
    // Sub-stage 8d: per-email EmailByUser policy fires on the 6th request within
    // the 60-minute sliding window.
    [Fact]
    public async Task Sixth_password_reset_request_in_same_hour_for_same_email_returns_429()
    {
        await using var factory = BuildFreshFactoryWithNoopEmail();
        var client = factory.CreateClient();
        var email = $"reset-{Guid.NewGuid():N}@example.invalid";

        for (var i = 0; i < 5; i++)
        {
            var ok = await PostResetRequest(factory, client, email);
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "the per-email bucket allows 5 requests before saturating");
        }

        var sixth = await PostResetRequest(factory, client, email);
        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────
    // Sub-stage 8d: per-IP backstop (EmailByIp via GlobalLimiter) fires on the 11th
    // request from the same IP, even when each request uses a distinct email so
    // the per-email bucket is not in play.
    [Fact]
    public async Task Eleventh_password_reset_request_from_same_ip_returns_429()
    {
        await using var factory = BuildFreshFactoryWithNoopEmail();
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var email = $"ip-reset-{i}-{Guid.NewGuid():N}@example.invalid";
            var ok = await PostResetRequest(factory, client, email);
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "each per-email bucket has 1/5 usage; only the per-IP bucket accumulates");
        }

        var eleventh = await PostResetRequest(
            factory, client, $"final-{Guid.NewGuid():N}@example.invalid");
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────
    // Stage 6.16 preservation: the EmailByUser limiter partitions by the
    // NORMALIZED EMAIL from the request body, not by UserId. This routes unknown
    // and known emails through the same limiter path on /password-reset/request,
    // so the 429 path performs the same number of Argon2id operations on both
    // branches — closing the timing channel Stage 6.16 closed.
    [Fact]
    public async Task Reset_request_for_unknown_email_and_known_email_have_equal_argon2id_call_count_on_429()
    {
        // WithArgon2idCounter spins up a fresh inner WebHost → empties the rate-limit
        // partition state (same effect as WithFreshRateLimiter — both call
        // WithWebHostBuilder under the hood). One call covers both needs.
        await using var factory = _factory.WithArgon2idCounter(out var counter);
        var client = factory.CreateClient();

        var knownEmail = $"counter-known-{Guid.NewGuid():N}@example.invalid";
        var unknownEmail = $"counter-unknown-{Guid.NewGuid():N}@example.invalid";

        // Register the known user via the test fixture so we don't burn the per-IP
        // bucket with /api/auth/register calls.
        await AuthTestFixture.RegisterUserAsync(factory, knownEmail);

        // Burn the EmailByUser bucket for the UNKNOWN email (5 successful calls).
        for (var i = 0; i < 5; i++)
        {
            var ok = await PostResetRequest(factory, client, unknownEmail);
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        counter.Reset();
        var unknown429 = await PostResetRequest(factory, client, unknownEmail);
        var unknownCount = counter.Count;
        unknown429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Burn the EmailByUser bucket for the KNOWN email branch in a SEPARATE fresh
        // inner host so the per-IP bucket is empty (the prior 6 calls saturated it to
        // 6/10; 5 more would hit the IP limit before we reach the per-email 6th call).
        // We also use a fresh GUID for the known email to avoid an Identity "already
        // exists" collision in the shared test database.
        var knownEmail2 = $"counter-known2-{Guid.NewGuid():N}@example.invalid";
        await using var factory2 = _factory.WithArgon2idCounter(out var counter2);
        var client2 = factory2.CreateClient();
        await AuthTestFixture.RegisterUserAsync(factory2, knownEmail2);

        for (var i = 0; i < 5; i++)
        {
            var ok = await PostResetRequest(factory2, client2, knownEmail2);
            ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        counter2.Reset();
        var known429 = await PostResetRequest(factory2, client2, knownEmail2);
        var knownCount = counter2.Count;
        known429.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        knownCount.Should().Be(unknownCount,
            "rate-limit denial must not introduce an Argon2id-call-count timing channel " +
            "between unknown and known emails — Stage 6.16 preservation. " +
            "knownCount={0}, unknownCount={1}",
            knownCount, unknownCount);
    }

    // ── Test 4 ──────────────────────────────────────────────────────────────
    // Spec-review gap fix: /email-change/request body has "newEmail", not "email",
    // so the EmailByUser middleware policy falls through to the authenticated
    // NameIdentifier claim. Two different users hitting /email-change/request must
    // NOT share a limiter bucket.
    [Fact]
    public async Task Email_change_request_partitions_by_user_id_not_by_email()
    {
        await using var factory = _factory
            .WithReplacedService<IEmailService>(new NoopEmailService())
            .WithWebHostBuilder(_ => { });

        var userA = await AuthTestFixture.RegisterUserAsync(
            _factory, $"a-{Guid.NewGuid():N}@example.invalid");
        var userB = await AuthTestFixture.RegisterUserAsync(
            _factory, $"b-{Guid.NewGuid():N}@example.invalid");

        // userA: 5 requests, each targeting a DIFFERENT new email so the per-newEmail
        // service-side bucket (which fires inside EmailChangeService) doesn't intervene.
        for (var i = 0; i < 5; i++)
        {
            var resp = await PostEmailChangeRequest(factory, userA, $"target-a-{i}-{Guid.NewGuid():N}@example.invalid");
            resp.StatusCode.Should().Be(HttpStatusCode.Accepted,
                "userA's 1..5th requests fill their EmailByUser bucket without saturating it");
        }

        // userA's 6th must 429 — middleware bucket keyed by userA's NameIdentifier.
        var a6 = await PostEmailChangeRequest(factory, userA, $"target-a-6-{Guid.NewGuid():N}@example.invalid");
        a6.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // userB's FIRST must succeed — bucket keyed by userB's NameIdentifier is fresh.
        var b1 = await PostEmailChangeRequest(factory, userB, $"target-b-1-{Guid.NewGuid():N}@example.invalid");
        b1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "userB has a separate EmailByUser bucket; the limiter must NOT collapse " +
            "two authenticated callers into one partition.");
    }

    // ── Test 5 ──────────────────────────────────────────────────────────────
    // Stage 8 post-review fix: /api/auth/email-change/request now has the
    // EmailByIp global-limiter gate via [ApplyEmailIpRateLimit] (same as
    // /password-reset/request) so an authenticated attacker with multiple
    // accounts cannot circumvent the per-user 5/hr by rotating accounts.
    [Fact]
    public async Task Eleventh_email_change_request_from_same_ip_returns_429()
    {
        // WithReplacedService(...).WithWebHostBuilder(_ => { }) gives a fresh inner
        // WebHost (and therefore empty rate-limit partition state) with a no-op
        // IEmailService — mirrors the existing partition test in this file.
        await using var factory = _factory
            .WithReplacedService<IEmailService>(new NoopEmailService())
            .WithWebHostBuilder(_ => { });

        // Register 11 distinct users on the OUTER factory (shares the test DB) so
        // each gets its own EmailByUser bucket — the limiter we want to hit is the
        // per-IP one, not the per-user one.
        var users = new List<ApplicationUser>();
        for (var i = 0; i < 11; i++)
        {
            var email = $"ip-emailchange-{i}-{Guid.NewGuid():N}@example.invalid";
            users.Add(await AuthTestFixture.RegisterUserAsync(_factory, email));
        }

        // The first 10 requests, each from a DIFFERENT user, should saturate the
        // per-IP bucket (10/hr) without hitting any per-user bucket (each at 1/5).
        for (var i = 0; i < 10; i++)
        {
            var resp = await PostEmailChangeRequest(factory, users[i],
                $"target-{Guid.NewGuid():N}@example.invalid");
            resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                "the first 10 requests from this IP should succeed (per-IP bucket of 10/hr)");
        }

        // 11th request from the 11th account should 429 on the per-IP gate.
        var eleventh = await PostEmailChangeRequest(factory, users[10],
            $"target-{Guid.NewGuid():N}@example.invalid");
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fresh inner host (fresh rate-limit partitions) with a no-op IEmailService so
    /// IEmailService.SendAsync calls don't bleed into the test's expectations.
    /// </summary>
    private WebApplicationFactory<Program> BuildFreshFactoryWithNoopEmail() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new NoopEmailService());
            }));

    private Task<HttpResponseMessage> PostResetRequest(
        WebApplicationFactory<Program> factory, HttpClient client, string email) =>
        AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });

    /// <summary>
    /// Mirror of <see cref="EmailChangeRateLimitTests"/>'s PostRequestAsync — builds an
    /// auth cookie with a fresh LastReauthAt so <c>[RequireRecentAuth]</c> passes,
    /// then sends a CSRF-validated POST /api/auth/email-change/request.
    /// </summary>
    private async Task<HttpResponseMessage> PostEmailChangeRequest(
        WebApplicationFactory<Program> factory, ApplicationUser user, string newEmail)
    {
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/request")
        {
            Content = JsonContent.Create(new { newEmail }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }
}
