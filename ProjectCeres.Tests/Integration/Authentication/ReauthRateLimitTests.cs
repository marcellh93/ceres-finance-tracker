using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class ReauthRateLimitTests : IClassFixture<RateLimitedAuthTestWebApplicationFactory>
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public ReauthRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<(HttpClient client, string sessionCookie, ApplicationUser user)>
        SetupAuthenticatedClientAsync(string emailPrefix)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, sessionCookie, user);
    }

    [Fact]
    public async Task Reauth_per_user_limit_returns_429_at_11th_attempt()
    {
        var (client, sessionCookie, user) = await SetupAuthenticatedClientAsync("rl-pu");

        // First reauth uses correct password to anchor the user; subsequent ones use wrong password to
        // ensure they don't accidentally lock the account before we hit the rate limit (the 10/min limit
        // should fire first because lockout is also at 10 failures — they race; use correct password).
        for (var i = 0; i < 10; i++)
        {
            var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
            {
                Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
            };
            req.Headers.Add("Cookie",
                $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
            req.Headers.Add(SessionConstants.CsrfHeaderName, header);
            var r = await client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var (csrf11, header11) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req11.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf11}");
        req11.Headers.Add(SessionConstants.CsrfHeaderName, header11);
        var resp11 = await client.SendAsync(req11);
        resp11.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resp11.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Reauth_per_user_partition_isolates_users()
    {
        var (clientA, sessionA, userA) = await SetupAuthenticatedClientAsync("rl-pa-a");
        var (clientB, sessionB, userB) = await SetupAuthenticatedClientAsync("rl-pa-b");

        // Burn user A's bucket.
        for (var i = 0; i < 10; i++)
        {
            var (csrf, header) = AuthTestFixture.MintCsrf(_factory, userA.Id);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
            {
                Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
            };
            req.Headers.Add("Cookie",
                $"{SessionConstants.SessionCookieName}={sessionA}; {SessionConstants.CsrfCookieName}={csrf}");
            req.Headers.Add(SessionConstants.CsrfHeaderName, header);
            await clientA.SendAsync(req);
        }

        // User B's bucket is independent — first reauth should succeed.
        var (csrfB, headerB) = AuthTestFixture.MintCsrf(_factory, userB.Id);
        var reqB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        reqB.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionB}; {SessionConstants.CsrfCookieName}={csrfB}");
        reqB.Headers.Add(SessionConstants.CsrfHeaderName, headerB);
        var respB = await clientB.SendAsync(reqB);
        respB.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
