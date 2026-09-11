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

[Collection("IntegrationParallel3")]
public class UserBlockedIpTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;

    public UserBlockedIpTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@block-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserBlockedIps.IgnoreQueryFilters().Where(b => b.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Authenticated_request_from_blocked_ip_returns_403_and_revokes_matching_sessions()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "b@block-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "b@block-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);

        // Add a UserBlockedIp row with the IP that the session was created from.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.IgnoreQueryFilters().FirstAsync(s => s.UserId == user.Id);
            db.UserBlockedIps.Add(new UserBlockedIp
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                IpAddress = session.IpCreatedAt,
                BlockedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        var resp = await client.SendAsync(probe);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.IgnoreQueryFilters().FirstAsync(s => s.UserId == user.Id);
            session.RevokedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Unblock_clears_the_middleware_403_for_a_previously_blocked_ip()
    {
        // Stage 12.5.4, the roadmap's block→unblock→no-longer-403 test. The middleware compares
        // context.Connection.RemoteIpAddress (empty string in the TestServer host) against the
        // blocked rows, so — like the sibling test above — we seed the block row with that same
        // recorded IP directly, drive the 403, then UNBLOCK through the service and confirm a
        // fresh login from that IP is no longer 403'd. Unblock restores FUTURE access; it does
        // not resurrect the sessions the block revoked, hence the re-login.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "unblock@block-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var firstSession = await LoginAsync(client, "unblock@block-test.local");
        string recordedIp;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.IgnoreQueryFilters().FirstAsync(s => s.UserId == user.Id);
            recordedIp = session.IpCreatedAt;
            db.UserBlockedIps.Add(new UserBlockedIp
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                IpAddress = recordedIp,
                BlockedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // Blocked: the request from that IP is 403'd (and its session revoked) by the middleware.
        var probe1 = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        probe1.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={firstSession}");
        (await client.SendAsync(probe1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Remove the block. The TestServer records an EMPTY connection IP, and TryUnblockIpAsync
        // correctly refuses a blank address (VALIDATION_ERROR) — the same reason the block was
        // seeded directly rather than through TryBlockIpAsync. So this leg deletes the row directly;
        // the service's unblock on a concrete IP is proven by the round-trip test below.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserBlockedIps.IgnoreQueryFilters()
                .Where(b => b.UserId == user.Id && b.IpAddress == recordedIp)
                .ExecuteDeleteAsync();
        }

        // A fresh login from the same IP now succeeds and its requests are no longer 403'd.
        var secondSession = await LoginAsync(client, "unblock@block-test.local");
        var probe2 = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        probe2.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={secondSession}");
        (await client.SendAsync(probe2)).StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Block_then_unblock_a_concrete_ip_round_trips_through_the_service()
    {
        // Exercises the service API on a concrete (non-empty) IP — the shape the /settings/sessions
        // UI drives. TryBlockIpAsync records it, GetBlockedIpsAsync surfaces it, TryUnblockIpAsync
        // removes it. (The empty-IP TestServer value can't go through TryBlockIpAsync — it correctly
        // rejects a blank address as VALIDATION_ERROR — so the middleware leg above seeds directly.)
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "roundtrip@block-test.local");
        const string ip = "203.0.113.42";

        using var scope = _factory.Services.CreateScope();
        var userScope = scope.ServiceProvider.GetRequiredService<ProjectCeres.Common.IUserScope>().EnterAs(user.Id);
        try
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.ISessionService>();

            // callerIp differs from the target, so the self-lockout guard does not refuse the block.
            (await sessions.TryBlockIpAsync(ip, "203.0.113.9")).IsSuccess.Should().BeTrue();
            (await sessions.GetBlockedIpsAsync()).Should().ContainSingle().Which.IpAddress.Should().Be(ip);

            (await sessions.TryUnblockIpAsync(ip)).IsSuccess.Should().BeTrue();
            (await sessions.GetBlockedIpsAsync()).Should().BeEmpty();
        }
        finally { userScope.Dispose(); }
    }

    [Fact]
    public async Task Unblock_of_an_address_the_caller_never_blocked_is_NOT_FOUND()
    {
        // IDOR-safe / idempotent-ish: unblocking an address with no block for this caller
        // fails NOT_FOUND rather than silently succeeding, and cannot touch another user's block.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "noblock@block-test.local");

        using var scope = _factory.Services.CreateScope();
        var userScope = scope.ServiceProvider.GetRequiredService<ProjectCeres.Common.IUserScope>().EnterAs(user.Id);
        try
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ProjectCeres.Services.ISessionService>();
            var result = await sessions.TryUnblockIpAsync("198.51.100.7");
            result.IsSuccess.Should().BeFalse();
            result.Error!.Value.Code.Should().Be("NOT_FOUND");
        }
        finally { userScope.Dispose(); }
    }

    private async Task<string> LoginAsync(HttpClient client, string email)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return ExtractSetCookie(loginResp, SessionConstants.SessionCookieName)!;
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
