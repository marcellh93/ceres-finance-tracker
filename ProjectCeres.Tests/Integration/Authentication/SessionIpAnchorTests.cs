using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 12.5.1 — IP-anchoring enforcement in SessionRevocationValidator. An anchored
/// session is rejected when the request IP differs from the session's origin IP.
///
/// The TestServer host records an EMPTY connection IP (RemoteIpAddress is null → ""),
/// so to force a mismatch we seed a CONCRETE IpCreatedAt on the anchored row — the same
/// technique UserBlockedIpTests uses for the block middleware. The negative control leaves
/// IpCreatedAt at the recorded value so it matches, proving anchoring does not reject a
/// same-IP request.
/// </summary>
[Collection("IntegrationTests")]
public class SessionIpAnchorTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public SessionIpAnchorTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@anchor-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Anchored_session_whose_ip_differs_from_the_request_is_rejected_401()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "mismatch@anchor-test.local");
        var (client, sessionCookie) = await LoginAsync("mismatch@anchor-test.local");

        // Anchor the session AND point its origin IP at an address the test host is not on,
        // so the validator's exact-match comparison fails on the next request.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserSessions.IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.IsIpAnchored, true)
                    .SetProperty(s => s.IpCreatedAt, "203.0.113.50"));
        }

        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        var probed = await client.SendAsync(probe);

        probed.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an anchored session used from a different IP must be signed out");
    }

    [Fact]
    public async Task Anchored_session_from_its_own_ip_still_authenticates()
    {
        // Negative control: anchoring must NOT reject a request from the anchored IP. The
        // TestServer records IpCreatedAt = "" and sends "" as the request IP, so leaving
        // IpCreatedAt untouched keeps them equal — the enforcement is a no-op here.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "match@anchor-test.local");
        var (client, sessionCookie) = await LoginAsync("match@anchor-test.local");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserSessions.IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsIpAnchored, true));
        }

        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        var probed = await client.SendAsync(probe);

        probed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "an anchored session used from its own IP must keep working");
    }

    [Fact]
    public async Task Unanchored_session_from_a_different_ip_still_authenticates()
    {
        // Anchoring is opt-in: a session left unanchored is never rejected on an IP change,
        // even when its recorded origin IP differs from the request IP.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "optout@anchor-test.local");
        var (client, sessionCookie) = await LoginAsync("optout@anchor-test.local");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserSessions.IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.IsIpAnchored, false)
                    .SetProperty(s => s.IpCreatedAt, "203.0.113.50"));
        }

        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        var probed = await client.SendAsync(probe);

        probed.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "an unanchored session must not be rejected on an IP change");
    }

    private async Task<(HttpClient Client, string SessionCookie)> LoginAsync(string email)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (client, ExtractSetCookie(loginResp, SessionConstants.SessionCookieName)!);
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
