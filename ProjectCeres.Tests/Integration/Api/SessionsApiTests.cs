using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Services;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel2")]
public class SessionsApiTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public SessionsApiTests(AuthTestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.IpCreatedAt.EndsWith(".sessions-api-test"))
            .ExecuteDeleteAsync();
        await db.UserBlockedIps
            .IgnoreQueryFilters()
            .Where(b => b.IpAddress.EndsWith(".sessions-api-test"))
            .ExecuteDeleteAsync();
        await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserAgent == "sessions-api-test")
            .ExecuteDeleteAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Logs in via HTTP, which stamps LastReauthAt (production login flow).
    /// Returns the session cookie value and the user.
    /// </summary>
    private async Task<(string SessionCookie, Microsoft.AspNetCore.Identity.IdentityUser<Guid> User)>
        LoginWithFreshReauthAsync(string emailSuffix)
    {
        var email = $"sessions-api-{emailSuffix}-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = false });
        var cookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (cookie, user);
    }

    /// <summary>
    /// Mints a cookie without LastReauthAt (stale/no reauth claim) for the given user.
    /// Also inserts a session row so the validator doesn't reject the ticket.
    /// </summary>
    private async Task<(string SessionCookie, Guid SessionId)>
        MintStaleAuthCookieAsync(ApplicationUser user)
    {
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);

        // Retrieve the session id that was inserted inside MintAuthCookieWithLastReauthAt.
        // We can't call it without the factory scope, but MintAuthCookieWithLastReauthAt
        // already inserted the row — we just need the sid from the cookie.
        // Instead: read the most recently inserted session for this user.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sid = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .FirstAsync();
        return (cookie, sid);
    }

    /// <summary>
    /// Builds an HttpClient with the given session cookie pre-set and a fresh CSRF pair.
    /// Returns the client and the CSRF cookie+header values.
    /// </summary>
    private (HttpClient Client, string CsrfCookie, string CsrfHeader) BuildClient(
        string sessionCookie, Guid userId)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = false });
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; " +
            $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        client.DefaultRequestHeaders.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return (client, csrfCookie, csrfHeader);
    }

    // ── Test 1 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_lists_own_active_sessions_and_marks_current_session()
    {
        var (sessionCookie, user) = await LoginWithFreshReauthAsync("list");
        var (client, _, _) = BuildClient(sessionCookie, user.Id);

        var resp = await client.GetAsync("/api/sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var sessions = body.EnumerateArray().ToList();
        sessions.Should().NotBeEmpty("at least the login session should be returned");

        var current = sessions.Where(s => s.GetProperty("isCurrent").GetBoolean()).ToList();
        current.Should().HaveCount(1, "exactly one session should be flagged as isCurrent");

        // All returned sessions must belong to the caller (no cross-user leak).
        // We verify this indirectly: the GET scopes to UserId per the controller code.
        // Every session must have required fields.
        foreach (var s in sessions)
        {
            s.GetProperty("id").GetGuid().Should().NotBe(Guid.Empty);
            // ipCreatedAt is "" in the test host (RemoteIpAddress is null on TestServer).
            s.GetProperty("ipCreatedAt").GetString().Should().NotBeNull();
            s.GetProperty("userAgent").GetString().Should().NotBeNull();
        }
    }

    // ── Test 2 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_revokes_session_and_that_cookie_no_longer_authenticates()
    {
        // Login to get Session A.
        var (sessionCookieA, userA) = await LoginWithFreshReauthAsync("revoke");
        var (clientA, _, _) = BuildClient(sessionCookieA, userA.Id);

        // List to get the session id.
        var listResp = await clientA.GetAsync("/api/sessions");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().ToList();
        var sessionId = sessions.First(s => s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id").GetGuid();

        // Now login AGAIN as the same user to get Session B, which we will use to
        // perform the revoke (Session A might also be the current one in Session B's
        // perspective, but the important thing is: Session A's cookie stops working).
        var emailB = $"sessions-api-revoke-b-{Guid.NewGuid():N}@example.com";
        // Re-use user — log in again via a second client to get a fresh session with reauth.
        var clientB = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = false });
        var sessionCookieB = await AuthTestFixture.LoginViaHttpAsync(
            _factory, clientB, userA.UserName!);
        var (clientBAuth, _, _) = BuildClient(sessionCookieB, userA.Id);

        // Use Session B to revoke Session A.
        var deleteResp = await clientBAuth.DeleteAsync($"/api/sessions/{sessionId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Session A's cookie should now fail authentication.
        var probeClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = false });
        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookieA}");
        var probeResp = await probeClient.SendAsync(probe);
        probeResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a revoked session cookie must be rejected");
    }

    // ── Test 3 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_another_users_session_returns_404_idor_guard()
    {
        // User A logs in, creates a session.
        var (sessionCookieA, userA) = await LoginWithFreshReauthAsync("idor-a");
        var (clientA, _, _) = BuildClient(sessionCookieA, userA.Id);

        // User B logs in separately.
        var (sessionCookieB, userB) = await LoginWithFreshReauthAsync("idor-b");

        // Find User B's session id.
        var (clientBForList, _, _) = BuildClient(sessionCookieB, userB.Id);
        var listResp = await clientBForList.GetAsync("/api/sessions");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bSessions = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().ToList();
        var bSessionId = bSessions.First().GetProperty("id").GetGuid();

        // User A tries to delete User B's session — must get 404 (IDOR guard).
        var deleteResp = await clientA.DeleteAsync($"/api/sessions/{bSessionId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-user session revoke must return 404 (IDOR guard)");

        // User B's session must still be active.
        var (clientBVerify, _, _) = BuildClient(sessionCookieB, userB.Id);
        var verifyResp = await clientBVerify.GetAsync("/api/sessions");
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "User B's session should still be valid after the failed cross-user revoke");
    }

    // ── Test 4 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_block_ip_inserts_UserBlockedIp_and_revokes_matching_ip_sessions()
    {
        var testIp = $"10.0.{DateTime.UtcNow.Millisecond}.1.sessions-api-test";

        var (sessionCookie, user) = await LoginWithFreshReauthAsync("blockip");

        // Directly insert a second session with the target IP so we can verify bulk-revoke.
        Guid targetSessionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetSessionId = Guid.NewGuid();
            db.UserSessions.Add(new UserSession
            {
                Id = targetSessionId,
                UserId = user.Id,
                IpCreatedAt = testIp,
                UserAgent = "sessions-api-test",
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsPersistent = false,
            });
            await db.SaveChangesAsync();
        }

        var (client, _, _) = BuildClient(sessionCookie, user.Id);
        var resp = await client.PostAsJsonAsync("/api/sessions/block-ip", new { ipAddress = testIp });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // UserBlockedIp row must exist.
        var blocked = await verifyDb.UserBlockedIps
            .IgnoreQueryFilters()
            .Where(b => b.UserId == user.Id && b.IpAddress == testIp)
            .SingleOrDefaultAsync();
        blocked.Should().NotBeNull("UserBlockedIp row must be inserted");

        // The session with that IP must be revoked.
        var revokedSession = await verifyDb.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.Id == targetSessionId)
            .SingleAsync();
        revokedSession.RevokedAt.Should().NotBeNull(
            "sessions with the blocked IP must be bulk-revoked");
    }

    // ── Test 4b ──────────────────────────────────────────────────────────────────

    // Blocking an already-blocked IP hits the unique index on
    // (UserId, IpAddress). Before ISessionService this reached the generic
    // handler as a 500; blocking is idempotent by nature, so the service now
    // no-ops the duplicate insert and still revokes matching sessions.
    [Fact]
    public async Task Post_block_ip_twice_is_idempotent_and_still_revokes()
    {
        var testIp = $"10.0.{DateTime.UtcNow.Millisecond}.2.sessions-api-dup-test";

        var (sessionCookie, user) = await LoginWithFreshReauthAsync("blockipdup");
        var (client, _, _) = BuildClient(sessionCookie, user.Id);

        var first = await client.PostAsJsonAsync("/api/sessions/block-ip", new { ipAddress = testIp });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.PostAsJsonAsync("/api/sessions/block-ip", new { ipAddress = testIp });
        second.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "blocking an already-blocked IP is idempotent, not an error");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.UserBlockedIps
            .IgnoreQueryFilters()
            .CountAsync(b => b.UserId == user.Id && b.IpAddress == testIp);
        rows.Should().Be(1, "the duplicate block must not insert a second row");
    }

    // A blank IP is rejected by the service before any write.
    [Fact]
    public async Task Post_block_ip_with_blank_ip_returns_422_and_writes_nothing()
    {
        var (sessionCookie, user) = await LoginWithFreshReauthAsync("blockipblank");
        var (client, _, _) = BuildClient(sessionCookie, user.Id);

        var resp = await client.PostAsJsonAsync("/api/sessions/block-ip", new { ipAddress = "   " });
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var any = await db.UserBlockedIps.IgnoreQueryFilters()
            .AnyAsync(b => b.UserId == user.Id && b.IpAddress == "   ");
        any.Should().BeFalse();
    }

    // ── Self-lockout guard ───────────────────────────────────────────────────────
    //
    // UserBlockedIpMiddleware 403s every authenticated request from a blocked IP,
    // and it runs AFTER authentication — so logging in again from that address
    // succeeds and the next request still 403s. No unblock endpoint exists. That
    // makes blocking your own address unrecoverable without database access, which
    // is why the service refuses it.

    [Fact]
    public async Task Block_ip_refuses_the_address_the_caller_is_connected_from()
    {
        var (_, user) = await LoginWithFreshReauthAsync("blockipself");

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var result = await sessions.TryBlockIpAsync("203.0.113.9", callerIpAddress: "203.0.113.9");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be("SELF_LOCKOUT");

        var written = await db.UserBlockedIps.IgnoreQueryFilters()
            .AnyAsync(b => b.UserId == user.Id && b.IpAddress == "203.0.113.9");
        written.Should().BeFalse("a refused block must not write the row it refused");
    }

    [Fact]
    public async Task Block_ip_still_allows_an_address_the_caller_is_not_connected_from()
    {
        var (_, user) = await LoginWithFreshReauthAsync("blockipother");

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Establish the user context the service reads from ICurrentUserAccessor.
        // Without it the row is written under Guid.Empty and RLS never sees it.
        var jobScope = scope.ServiceProvider.GetRequiredService<IBackgroundJobScope>();
        Result result = default!;
        await jobScope.RunAsync(user.Id, nameof(Block_ip_still_allows_an_address_the_caller_is_not_connected_from),
            async () => { result = await sessions.TryBlockIpAsync("198.51.100.7", callerIpAddress: "203.0.113.9"); });

        result.IsSuccess.Should().BeTrue("the guard must only refuse the caller's own address");

        var written = await db.UserBlockedIps.IgnoreQueryFilters()
            .AnyAsync(b => b.UserId == user.Id && b.IpAddress == "198.51.100.7");
        written.Should().BeTrue();
    }

    // The comparison must not be defeated by casing — IPv6 addresses are commonly
    // written in either case and the two forms denote the same host.
    [Fact]
    public async Task Block_ip_self_guard_is_case_insensitive()
    {
        await LoginWithFreshReauthAsync("blockipcase");

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();

        var result = await sessions.TryBlockIpAsync(
            "2001:DB8::CAFE", callerIpAddress: "2001:db8::cafe");

        result.Error!.Value.Code.Should().Be("SELF_LOCKOUT");
    }

    // A null caller IP must not silently disable the guard's counterpart: with no
    // known caller address the block proceeds, which is the pre-existing behaviour.
    [Fact]
    public async Task Block_ip_proceeds_when_the_caller_address_is_unknown()
    {
        var (_, user) = await LoginWithFreshReauthAsync("blockipnullcaller");

        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jobScope = scope.ServiceProvider.GetRequiredService<IBackgroundJobScope>();
        Result result = default!;
        await jobScope.RunAsync(user.Id, nameof(Block_ip_proceeds_when_the_caller_address_is_unknown),
            async () => { result = await sessions.TryBlockIpAsync("198.51.100.42", callerIpAddress: null); });

        result.IsSuccess.Should().BeTrue();
        (await db.UserBlockedIps.IgnoreQueryFilters()
            .AnyAsync(b => b.UserId == user.Id && b.IpAddress == "198.51.100.42"))
            .Should().BeTrue();
    }

    // ── IP anchor (Stage 12.5.1) ──────────────────────────────────────────────────

    [Fact]
    public async Task Post_anchor_sets_IsIpAnchored_on_the_callers_own_session()
    {
        var (sessionCookie, user) = await LoginWithFreshReauthAsync("anchor");
        var (client, _, _) = BuildClient(sessionCookie, user.Id);

        // The current session's id, from the list.
        var listResp = await client.GetAsync("/api/sessions");
        var sessionId = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First(s => s.GetProperty("isCurrent").GetBoolean())
            .GetProperty("id").GetGuid();

        var resp = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/anchor", new { anchored = true });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // AsNoTracking on both reads: a tracking query would return the SAME identity-map
        // instance on the second read, masking the toggle-off DB write behind the stale
        // first-read value.
        var row = await db.UserSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == sessionId);
        row.IsIpAnchored.Should().BeTrue("anchoring the caller's own session must set the flag");

        // And toggling it back off is honoured.
        var off = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/anchor", new { anchored = false });
        off.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reread = await db.UserSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == sessionId);
        reread.IsIpAnchored.Should().BeFalse("toggling the anchor back off must clear the flag");
    }

    [Fact]
    public async Task Post_anchor_on_another_users_session_returns_404_idor_guard()
    {
        var (sessionCookieA, userA) = await LoginWithFreshReauthAsync("anchor-idor-a");
        var (clientA, _, _) = BuildClient(sessionCookieA, userA.Id);

        var (sessionCookieB, userB) = await LoginWithFreshReauthAsync("anchor-idor-b");
        var (clientBForList, _, _) = BuildClient(sessionCookieB, userB.Id);
        var bListResp = await clientBForList.GetAsync("/api/sessions");
        var bSessionId = (await bListResp.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().First().GetProperty("id").GetGuid();

        var resp = await clientA.PostAsJsonAsync($"/api/sessions/{bSessionId}/anchor", new { anchored = true });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "anchoring another user's session must be indistinguishable from an unknown id (IDOR guard)");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.UserSessions.IgnoreQueryFilters().SingleAsync(s => s.Id == bSessionId);
        row.IsIpAnchored.Should().BeFalse("the refused cross-user anchor must not have flipped the flag");
    }

    // ── Test 5 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_without_recent_auth_returns_401_REAUTH_REQUIRED()
    {
        var user = await AuthTestFixture.RegisterUserAsync(
            _factory, $"sessions-api-noreauth-{Guid.NewGuid():N}@example.com");

        // Mint a cookie without LastReauthAt.
        var (staleCookie, _) = await MintStaleAuthCookieAsync(user);

        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/sessions");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={staleCookie}; " +
            $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }
}
