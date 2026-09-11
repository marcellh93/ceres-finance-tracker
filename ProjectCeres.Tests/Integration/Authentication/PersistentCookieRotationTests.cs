using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel4")]
public class PersistentCookieRotationTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PersistentCookieRotationTests(AuthTestWebApplicationFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users
            .Where(u => u.Email!.EndsWith("@persist-test.local") || u.Email!.EndsWith("@persist-stamp-test.local"))
            .ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Persistent_token_is_rotated_on_use_and_old_token_is_rejected()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "p@persist-test.local");

        // Step A — login with rememberMe; capture __Host-Persist value.
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "p@persist-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = true
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await loginClient.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var oldPersist = ExtractCookie(loginResp.Headers.GetValues("Set-Cookie"), "__Host-Persist");
        oldPersist.Should().NotBeNull();

        // Step B — clear the session cookie, send only the persistent cookie. Handler rotates.
        var rotateClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var rotateReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        rotateReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var rotateResp = await rotateClient.SendAsync(rotateReq);
        var rotateSetCookies = rotateResp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        var newPersist = ExtractCookie(rotateSetCookies, "__Host-Persist");
        newPersist.Should().NotBeNull();
        newPersist.Should().NotBe(oldPersist);

        // Step C — replay the OLD token; should not authenticate.
        var replayClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var replayReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        replayReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var replay = await replayClient.SendAsync(replayReq);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSameCookie_RotateExactlyOnce()
    {
        // Set up a rememberMe session via the password-step login path.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "race@persist-test.local");
        var loginClient = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();

        // Extract the __Host-Persist cookie value the server just issued.
        var setCookies = loginResp.Headers.GetValues("Set-Cookie").ToList();
        var persistCookieLine = setCookies.First(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="));
        var persistValue = persistCookieLine.Split(';')[0].Substring(SessionConstants.PersistentCookieName.Length + 1);

        // Two parallel requests, each with ONLY the persist cookie (no session cookie),
        // hitting an authenticated endpoint that triggers the rotation middleware.
        async Task<HttpResponseMessage> SendAsync()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
            req.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={persistValue}");
            return await client.SendAsync(req);
        }

        var t1 = SendAsync();
        var t2 = SendAsync();
        await Task.WhenAll(t1, t2);

        // Assert exactly one new persistent session row was created (the original was revoked).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activePersistent = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id && s.IsPersistent && s.RevokedAt == null)
            .CountAsync();
        activePersistent.Should().Be(1, "concurrent rotation must produce exactly one new active persistent session");
    }

    [Fact]
    public async Task RotationDoesNotAuthenticateCurrentRequest_ReturnsFreshCookieFor401()
    {
        // Set up a rememberMe session.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "stamp@persist-stamp-test.local");
        var loginClient = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();

        var setCookies = loginResp.Headers.GetValues("Set-Cookie").ToList();
        var persistCookieLine = setCookies.First(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="));
        var persistValue = persistCookieLine.Split(';')[0].Substring(SessionConstants.PersistentCookieName.Length + 1);

        // Now hit an authenticated endpoint with ONLY the persist cookie (no __Host-Session).
        // The middleware rotates the persistent cookie and signs into Identity (issuing a
        // fresh __Host-Session for the next request) — but per Gap 11, this current request
        // should NOT be authenticated. The fallback policy returns 401.
        var noSessionClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var firstReq = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        firstReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={persistValue}");

        var firstResp = await noSessionClient.SendAsync(firstReq);
        firstResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "rotation must NOT authenticate this exact request — the freshly-set cookie carries the next request");

        // The response MUST carry a fresh __Host-Session cookie.
        var firstSetCookies = firstResp.Headers.TryGetValues("Set-Cookie", out var c1) ? c1.ToList() : new List<string>();
        firstSetCookies.Should().Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="),
            "rotation must issue a fresh __Host-Session cookie even though this request was rejected");

        // The response MUST also carry the rotated __Host-Persist.
        firstSetCookies.Should().Contain(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="),
            "rotation must issue the new __Host-Persist cookie");
    }

    [Fact]
    public async Task StaleSessionTicket_AfterRealExpiry_WithPersistCookie_StillRotatesAndAuthenticates()
    {
        // Reproduces the user-reported 2026-05-22 bug: log in with Remember Me,
        // wait past ExpireTimeSpan, browser sends BOTH the (now-stale) session
        // ticket AND the still-valid persist cookie. The middleware MUST rotate.
        //
        // This is different from the StaleSessionCookieWithValidPersist test
        // above — that one sends a BOGUS session cookie value. This one sends
        // a REAL session ticket that was issued legitimately and only became
        // stale by clock-time. The cookie auth pipeline will attempt to
        // decrypt and validate it, fail (expired), and then we observe what
        // the middleware does on this request.

        // Build a factory variant where the application cookie expires in 1 second
        // and does NOT slide, so we can force the expiry deterministically.
        await using var shortTtlFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<CookieAuthenticationOptions>(
                    IdentityConstants.ApplicationScheme,
                    opts =>
                    {
                        opts.ExpireTimeSpan = TimeSpan.FromSeconds(1);
                        opts.SlidingExpiration = false;
                    });
            });
        });

        var user = await AuthTestFixture.RegisterUserAsync(shortTtlFactory, "ticketexpiry@persist-test.local");

        // Log in with Remember Me — get both __Host-Session (real, signed) and __Host-Persist.
        var loginClient = shortTtlFactory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            shortTtlFactory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();

        var loginSetCookies = loginResp.Headers.GetValues("Set-Cookie").ToList();
        var sessionValue = ExtractCookie(loginSetCookies, SessionConstants.SessionCookieName);
        var persistValue = ExtractCookie(loginSetCookies, SessionConstants.PersistentCookieName);
        sessionValue.Should().NotBeNull();
        persistValue.Should().NotBeNull();

        // Wait past the 1-second ExpireTimeSpan so the session ticket is provably
        // stale at the server's clock.
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Make an authenticated request with BOTH cookies — the browser's real behaviour.
        var client = shortTtlFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        req.Headers.Add(
            "Cookie",
            $"{SessionConstants.SessionCookieName}={sessionValue}; " +
            $"{SessionConstants.PersistentCookieName}={persistValue}");
        var resp = await client.SendAsync(req);

        // The middleware MUST issue a fresh __Host-Session cookie.
        var respSetCookies = resp.Headers.TryGetValues("Set-Cookie", out var c) ? c.ToList() : new List<string>();
        respSetCookies.Should().Contain(s => s.StartsWith($"{SessionConstants.SessionCookieName}="),
            "the stale session ticket made the request unauthenticated, persist was valid — " +
            "middleware MUST rotate and issue a fresh __Host-Session for the next request");
        respSetCookies.Should().Contain(s => s.StartsWith($"{SessionConstants.PersistentCookieName}="),
            "rotation MUST also issue the new __Host-Persist");

        // The 401 must carry the X-Ceres-Cookie-Rotated marker header — the SPA's
        // silent-401 seam keys on this to NOT fire the unauthenticated handler
        // for rotation-handshake 401s. Without it, the SPA would drop to 'anon'
        // and redirect to /login before the browser ever sent the retry that
        // carries the freshly-issued session cookie.
        resp.Headers.Contains(SessionConstants.CookieRotatedHeader).Should().BeTrue(
            $"the rotation 401 MUST carry the {SessionConstants.CookieRotatedHeader} header " +
            "so the SPA distinguishes rotation-handshake from genuine session expiry");
    }

    [Fact]
    public async Task StaleSessionCookieWithValidPersist_RotatesAndIssuesFreshSession()
    {
        // This is the bug the user hit on 2026-05-22 / 23: idle past 30 min with
        // Remember Me ticked. The browser keeps sending __Host-Session even after
        // its server-side ticket (ExpireTimeSpan) has lapsed, because ExpireTimeSpan
        // is the ticket validity, not the cookie's browser-side Expires attribute.
        // The middleware must rotate anyway when the session cookie no longer
        // authenticates the request AND __Host-Persist is still valid.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "stale@persist-test.local");
        var loginClient = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();

        var setCookies = loginResp.Headers.GetValues("Set-Cookie").ToList();
        var persistValue = ExtractCookie(setCookies, SessionConstants.PersistentCookieName);
        persistValue.Should().NotBeNull();

        // Construct a request that sends BOTH cookies, where __Host-Session is a
        // bogus / stale value (simulating an expired ticket the browser still has).
        // The middleware must NOT short-circuit just because the session cookie
        // header is present — it must check whether the request actually
        // authenticated, then rotate when persist is valid.
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        req.Headers.Add(
            "Cookie",
            $"{SessionConstants.SessionCookieName}=stale-encrypted-payload-no-longer-valid; " +
            $"{SessionConstants.PersistentCookieName}={persistValue}");
        var resp = await client.SendAsync(req);

        // The response must carry a fresh __Host-Session cookie (rotation happened).
        var respSetCookies = resp.Headers.TryGetValues("Set-Cookie", out var c) ? c.ToList() : new List<string>();
        respSetCookies.Should().Contain(s => s.StartsWith($"{SessionConstants.SessionCookieName}="),
            "rotation must issue a fresh __Host-Session cookie even when the stale session cookie was also sent");
        respSetCookies.Should().Contain(s => s.StartsWith($"{SessionConstants.PersistentCookieName}="),
            "rotation must issue the new __Host-Persist cookie");
    }

    // Stage 12.5.1 — the anchor must survive the persistent-rotation hop, which runs BEFORE
    // SessionRevocationValidator and so must enforce the anchor itself.

    [Fact]
    public async Task Anchored_persistent_session_is_not_rotated_from_a_different_ip()
    {
        // Seed a rememberMe login, then anchor its persistent row to an IP the TestServer is
        // not on (RemoteIpAddress is null → ""). A rotation request therefore arrives from a
        // MISMATCHED IP and must be refused: no new __Host-Persist cookie, no fresh session —
        // otherwise a stolen persistent cookie replayed elsewhere would rotate past the anchor.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "anchor-rotate@persist-test.local");
        var loginClient = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();
        var oldPersist = ExtractCookie(loginResp.Headers.GetValues("Set-Cookie"), SessionConstants.PersistentCookieName);
        oldPersist.Should().NotBeNull();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.UserSessions.IgnoreQueryFilters()
                .Where(s => s.UserId == user.Id && s.IsPersistent && s.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.IsIpAnchored, true)
                    .SetProperty(s => s.IpCreatedAt, "203.0.113.50"));
        }

        var rotateClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var rotateReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        rotateReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var rotateResp = await rotateClient.SendAsync(rotateReq);

        rotateResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var setCookies = rotateResp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        setCookies.Should().NotContain(s => s.StartsWith($"{SessionConstants.PersistentCookieName}="),
            "an anchored persistent session must NOT rotate from a mismatched IP");

        // The original row is untouched (not revoked): the rotation was refused, not consumed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.UserSessions.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.UserId == user.Id && s.IsPersistent).ToListAsync();
            rows.Should().ContainSingle().Which.RevokedAt.Should().BeNull(
                "a refused rotation must not revoke the anchored row");
        }
    }

    [Fact]
    public async Task Rotation_carries_the_ip_anchor_forward_to_the_new_session()
    {
        // Anchor the persistent row but leave IpCreatedAt at the TestServer value ("") so the
        // rotation IP matches and rotation proceeds. The NEW row must inherit IsIpAnchored —
        // otherwise the anchor silently evaporates on the first return visit.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "anchor-carry@persist-test.local");
        var loginClient = _factory.CreateClient();
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
        loginResp.EnsureSuccessStatusCode();
        var oldPersist = ExtractCookie(loginResp.Headers.GetValues("Set-Cookie"), SessionConstants.PersistentCookieName);
        oldPersist.Should().NotBeNull();

        Guid oldSessionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserSessions.IgnoreQueryFilters()
                .SingleAsync(s => s.UserId == user.Id && s.IsPersistent && s.RevokedAt == null);
            oldSessionId = row.Id;
            row.IsIpAnchored = true;
            await db.SaveChangesAsync();
        }

        var rotateClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var rotateReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        rotateReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var rotateResp = await rotateClient.SendAsync(rotateReq);
        var setCookies = rotateResp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        setCookies.Should().Contain(s => s.StartsWith($"{SessionConstants.PersistentCookieName}="),
            "a same-IP anchored session still rotates");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var newRow = await db.UserSessions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(s => s.UserId == user.Id && s.IsPersistent && s.RevokedAt == null && s.Id != oldSessionId);
            newRow.IsIpAnchored.Should().BeTrue("the rotated session must inherit the anchor");
        }
    }

    private static string? ExtractCookie(IEnumerable<string> setCookies, string name)
    {
        foreach (var c in setCookies)
        {
            var first = c.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == name) return first[(eq + 1)..];
        }
        return null;
    }
}
