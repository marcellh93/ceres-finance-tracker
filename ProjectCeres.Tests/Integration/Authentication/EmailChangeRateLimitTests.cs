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

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class EmailChangeRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public EmailChangeRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => AuthTestTokenCleanup.DeleteAllTestTokensAsync(_factory);

    private static async Task<HttpResponseMessage> PostConfirmAsync(
        WebApplicationFactory<Program> factory, RateLimitedAuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/confirm")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PostRevokeAsync(
        WebApplicationFactory<Program> factory, RateLimitedAuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/revoke")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostRequestAsync(
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

    // ── Test #33 ────────────────────────────────────────────────────────────
    // /confirm uses AuthLoginByIp policy. 11th call from same IP in 60s → 429.
    [Fact]
    public async Task Eleventh_confirm_from_same_IP_in_one_minute_returns_429()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        for (var i = 0; i < 10; i++)
        {
            var resp = await PostConfirmAsync(factory, _factory, $"any-token-{i}-{Guid.NewGuid():N}");
            // Each returns 401 (invalid token) — that's fine, the rate limiter counts
            // accepted requests not 200s.
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejected = await PostConfirmAsync(factory, _factory, $"any-token-11-{Guid.NewGuid():N}");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test #34 ────────────────────────────────────────────────────────────
    // /revoke uses AuthLoginByIp policy. 11th call from same IP within window → 429.
    [Fact]
    public async Task Eleventh_revoke_from_same_IP_in_one_minute_returns_429()
    {
        // Stays on production 60s window. The sister test
        // Eleventh_confirm_from_same_IP_in_one_minute_returns_429 already verifies
        // the same AuthLoginByIp policy on /confirm; if /confirm passes the policy is
        // working. Production-window pattern preserved for parity with the sister test.
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        for (var i = 0; i < 10; i++)
        {
            var resp = await PostRevokeAsync(factory, _factory, $"any-token-{i}-{Guid.NewGuid():N}");
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejected = await PostRevokeAsync(factory, _factory, $"any-token-11-{Guid.NewGuid():N}");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── Test #35 ────────────────────────────────────────────────────────────
    // Service-side per-new-email window (5/hour). 6th /request against same
    // newEmail → 429 RATE_LIMITED with Retry-After.
    [Fact]
    public async Task Sixth_request_against_same_newEmail_within_one_hour_returns_429_with_RetryAfter()
    {
        await using var factory = _factory
            .WithReplacedService<IEmailService>(new NoopEmailService())
            .WithWebHostBuilder(_ => { });

        var oldEmail = $"old-rate-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-rate-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        for (var i = 0; i < 5; i++)
        {
            var resp = await PostRequestAsync(factory, user, newEmail);
            resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var rejected = await PostRequestAsync(factory, user, newEmail);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("RATE_LIMITED");
    }

    // ── Test #36 ────────────────────────────────────────────────────────────
    // Per-email bucket is keyed by NEW email, not OLD email. Two distinct users
    // targeting the same newEmail share the bucket. (At /request time the
    // collision check returns 422 EMAIL_ALREADY_IN_USE if the new email is
    // taken — so for this test both users target an unregistered new email.
    // The point: the same newEmail string burns the bucket regardless of which
    // authenticated user submitted it.)
    [Fact]
    public async Task Per_email_rate_limit_is_keyed_by_newEmail_not_oldEmail()
    {
        await using var factory = _factory
            .WithReplacedService<IEmailService>(new NoopEmailService())
            .WithWebHostBuilder(_ => { });

        var sharedNewEmail = $"shared-target-{Guid.NewGuid():N}@example.com";
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, $"a-{Guid.NewGuid():N}@example.com");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, $"b-{Guid.NewGuid():N}@example.com");

        // 3 requests from A toward sharedNewEmail
        for (var i = 0; i < 3; i++)
        {
            var resp = await PostRequestAsync(factory, userA, sharedNewEmail);
            resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
        // 2 requests from B toward sharedNewEmail
        for (var i = 0; i < 2; i++)
        {
            var resp = await PostRequestAsync(factory, userB, sharedNewEmail);
            resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        // 6th total against sharedNewEmail — from A — should 429.
        var rejected = await PostRequestAsync(factory, userA, sharedNewEmail);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the bucket is keyed by NEW email; both users' requests share it");

        // Sanity: same userA targeting a DIFFERENT newEmail should still succeed.
        var differentNewEmail = $"different-target-{Guid.NewGuid():N}@example.com";
        var different = await PostRequestAsync(factory, userA, differentNewEmail);
        different.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a new bucket exists for each distinct newEmail");
    }
}
