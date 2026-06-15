using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthGateTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public ReauthGateTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> CallGatedEndpointAsync(
        ApplicationUser user, long? lastReauthAtUnix)
    {
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, lastReauthAtUnix);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    // ── Test #16 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_fresh_claim_succeeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-fresh-{Guid.NewGuid():N}@example.com");
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var resp = await CallGatedEndpointAsync(user, fresh);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "fresh claim within 5-min window should pass the gate; /mfa/enroll returns 200 OK");
    }

    // ── Test #17 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_no_claim_returns_401_REAUTH_REQUIRED()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-no-{Guid.NewGuid():N}@example.com");

        var resp = await CallGatedEndpointAsync(user, null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    // ── Test #18 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_stale_claim_returns_401_REAUTH_REQUIRED()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-stale-{Guid.NewGuid():N}@example.com");
        var stale = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();  // 1 second past the boundary

        var resp = await CallGatedEndpointAsync(user, stale);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    // ── Test #19 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_claim_at_boundary_succeeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-bnd-{Guid.NewGuid():N}@example.com");
        var boundary = DateTimeOffset.UtcNow.AddSeconds(-300).ToUnixTimeSeconds();  // exactly at the window

        var resp = await CallGatedEndpointAsync(user, boundary);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "claim at age=300s exactly should pass (boundary inclusive)");
    }

    // ── Test #20 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_future_claim_returns_401_REAUTH_REQUIRED()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-fut-{Guid.NewGuid():N}@example.com");
        var future = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();

        var resp = await CallGatedEndpointAsync(user, future);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    // ── Test #21 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_with_malformed_claim_returns_401_REAUTH_REQUIRED()
    {
        // MintAuthCookieWithLastReauthAt only takes a long?. Build a separate helper inline:
        // mint a cookie that contains a non-numeric LastReauthAt claim.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-mal-{Guid.NewGuid():N}@example.com");

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = sp };
        http.Items[SessionConstants.LastReauthAtItemKey] = "not-a-number";
        var sid = Guid.NewGuid();
        http.Items[SessionConstants.PendingSessionItemKey] = sid;
        var accessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        if (accessor is not null) accessor.HttpContext = http;

        var pcf = sp.GetRequiredService<Microsoft.AspNetCore.Identity.IUserClaimsPrincipalFactory<ApplicationUser>>();
        var principal = await pcf.CreateAsync(user);

        var db = sp.GetRequiredService<Data.AppDbContext>();
        db.UserSessions.Add(new UserSession
        {
            Id = sid, UserId = user.Id, IpCreatedAt = "127.0.0.1", UserAgent = "test",
            CreatedAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow, IsPersistent = false,
        });
        await db.SaveChangesAsync();

        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
            principal,
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = false },
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        var dpProvider = sp.GetRequiredService<IDataProtectionProvider>();
        var protector = dpProvider.CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
            "v2");
        var format = new Microsoft.AspNetCore.Authentication.TicketDataFormat(protector);
        var cookie = format.Protect(ticket);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    // ── Test #22 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_unauthenticated_request_returns_401_with_default_handler_not_REAUTH_REQUIRED()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain("REAUTH_REQUIRED",
            "unauthenticated requests should be handled by the default handler, not the RecentAuth envelope");
    }

    // ── Test #23 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gated_endpoint_after_successful_reauth_succeeds()
    {
        var email = $"gate-pr-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        // Stage 1: legacy session (no LastReauthAt). Gated endpoint should 401 REAUTH_REQUIRED.
        var noClaimCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var gatedReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        gatedReq1.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={noClaimCookie}; {SessionConstants.CsrfCookieName}={csrf1}");
        gatedReq1.Headers.Add(SessionConstants.CsrfHeaderName, header1);
        var resp1 = await client.SendAsync(gatedReq1);
        resp1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp1.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");

        // Stage 2: step up via /api/auth/reauth.
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var reauthReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        reauthReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={noClaimCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        reauthReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var reauthResp = await client.SendAsync(reauthReq);
        reauthResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var refreshedCookie = reauthResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
            .Split(';')[0]
            .Substring(SessionConstants.SessionCookieName.Length + 1);

        // Stage 3: retry the gated request with the refreshed cookie.
        var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var gatedReq2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        gatedReq2.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={refreshedCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
        gatedReq2.Headers.Add(SessionConstants.CsrfHeaderName, header3);
        var resp2 = await client.SendAsync(gatedReq2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
