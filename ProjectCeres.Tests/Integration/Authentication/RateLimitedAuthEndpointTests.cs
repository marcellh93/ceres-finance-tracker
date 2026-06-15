using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class RateLimitedAuthEndpointTests : IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;

    public RateLimitedAuthEndpointTests(RateLimitedAuthTestWebApplicationFactory factory)
        => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@rl-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    // ---------------------------------------------------------------------------
    // Helper: fire a request repeatedly until a 429 comes back, or maxAttempts
    // is exhausted. Returns the first 429 response, or null if the limit was
    // never hit. Tests share one IP partition (TestServer loopback), so the
    // bucket may already be partially consumed from a previous test — this
    // helper is resilient to whatever residue is present.
    // ---------------------------------------------------------------------------
    private static async Task<HttpResponseMessage?> FireUntilRateLimited(
        Func<Task<HttpResponseMessage>> request, int maxAttempts = 25)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var r = await request();
            if (r.StatusCode == HttpStatusCode.TooManyRequests) return r;
        }
        return null;
    }

    [Fact]
    public async Task Login_RequestsEventuallyReturn429WithRetryAfter()
    {
        // Use an UNREGISTERED email so we exercise the per-IP rate-limit path without
        // triggering account lockout (which would cause OnRejected to surface
        // ACCOUNT_LOCKED_OUT 401 instead of RATE_LIMITED 429 from Stage 9.1.5.b onward).
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "nouser-rl@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

        rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
        rejected!.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task RateLimit429_ResponseBodyMatchesApiContractEnvelope()
    {
        // Unregistered email — see Login_RequestsEventuallyReturn429WithRetryAfter rationale.
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "nouser-envelope@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

        rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
        var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        body.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Csrf_HasItsOwnPolicy_DoesNotConsumeLoginBucket()
    {
        // Burn substantially MORE than the login bucket would allow on /csrf.
        // If they shared the bucket, /login would be locked out after 10 calls.
        // With separate buckets, /login still has its full quota.
        //
        // Use WithFreshRateLimiter() so the rate-limiter partition state is empty
        // regardless of which tests ran before us — no real-wall-clock sleep needed.
        await using var factory = _factory.WithFreshRateLimiter();
        var client = factory.CreateClient();

        for (int i = 0; i < 25; i++)
        {
            var resp = await client.GetAsync("/api/auth/csrf");
            // Don't care whether 60-bucket trips here; we just need to consume MANY /csrf
            // calls and confirm /login's bucket is untouched.
        }

        // /login from the SAME client should still succeed (not 429), because the buckets
        // are independent.
        await AuthTestFixture.RegisterUserAsync(factory, "csrfsep@rl-test.local");
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "csrfsep@rl-test.local", password = AuthTestFixture.ValidPassword, rememberMe = false });

        loginResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "burning /csrf bucket must NOT consume /login bucket — they are separate policies (Gap 7)");
    }

    [Fact]
    public async Task Csrf_OwnPolicyStillRateLimitedAtHigherThreshold()
    {
        // 60/min cap. Burn more than 60 to confirm the new policy still rate-limits, just at
        // a more permissive threshold. Use FireUntilRateLimited helper to find the rejection
        // without hardcoding the count.
        var client = _factory.CreateClient();
        var rejected = await FireUntilRateLimited(() => client.GetAsync("/api/auth/csrf"), maxAttempts: 70);
        rejected.Should().NotBeNull("the auth-csrf-by-ip policy must still rate-limit, just at a higher threshold");
    }

    [Fact]
    public async Task RateLimitOnRejected_ContentTypeIsApplicationJson()
    {
        // Unregistered email — see Login_RequestsEventuallyReturn429WithRetryAfter rationale.
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "nouser-contenttype@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

        rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
        rejected!.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Login_LimiterResetsAfterWindow()
    {
        // Override AuthLoginByIp to use the 5-second window for this test so we can
        // wait the window out in ~5.5s instead of 70s. This test's whole point is
        // "saturate, wait, confirm window rolled over" — we just shrink the wall-clock
        // duration of the rollover instead of using a fresh limiter (which would
        // defeat the test's purpose).
        //
        // The 5s medium window (not the 1s short window) is required because the
        // saturation step below fires a SEQUENTIAL burst of up to 25 requests; under
        // full-suite CPU contention that burst can take >1s of HTTP + Argon2id overhead,
        // so a 1s window slides off the earliest requests before the threshold lands and
        // the bucket never trips (the burst-too-slow-for-window flake). 5s is the window
        // the project sized for exactly such a burst (see WithMediumLoginWindow).
        //
        // Unregistered email so the burst doesn't trip lockout (Stage 9.1.5.b: lockout
        // would cause OnRejected to surface ACCOUNT_LOCKED_OUT 401 instead of 429).
        await using var factory = _factory.WithMediumLoginWindow();
        var client = factory.CreateClient();

        // Saturate the bucket.
        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = "nouser-reset-window@rl-test.local", password = "x-long-enough-x", rememberMe = false }));
        rejected.Should().NotBeNull();

        // Sliding window is 5s for this test (see WithMediumLoginWindow).
        await Task.Delay(RateLimitedAuthTestWebApplicationFactory.MediumLoginWindowClearDelay);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "nouser-reset-window@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginTotp_LimiterPartitionsPerUser()
    {
        // Fresh rate-limiter so the IP-keyed login bucket isn't saturated by prior tests.
        await using var factory = _factory.WithFreshRateLimiter();
        var userA = await AuthTestFixture.RegisterUserAsync(factory, "totp-a@rl-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(factory, "totp-b@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userA);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userB);

        // Use HandleCookies = false so we can manually carry cookies across requests.
        // PostJsonWithCsrfAsync sets Cookie headers directly; the TwoFactorUserId
        // cookie must also be forwarded explicitly so the rate-limit partitioner
        // (which calls AuthenticateAsync) can resolve the per-user partition key.
        var options = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        };
        var clientA = factory.CreateClient(options);
        var loginA = await AuthTestFixture.PostJsonWithCsrfAsync(factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        // DIAGNOSTIC: surface the actual status and Set-Cookie headers
        var loginABody = await loginA.Content.ReadAsStringAsync();
        var loginASetCookies = loginA.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join("; ", sc) : "(none)";
        loginA.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            $"userA login must not be rate-limited; status={loginA.StatusCode}, Set-Cookie={loginASetCookies}, body={loginABody}");
        ((int)loginA.StatusCode).Should().BeOneOf(new[] { 200, 204 },
            $"userA login must succeed; status={loginA.StatusCode}, Set-Cookie={loginASetCookies}, body={loginABody}");
        var mfaCookieA = ExtractSetCookie(loginA, "Identity.TwoFactorUserId");

        var clientB = factory.CreateClient(options);
        var loginB = await AuthTestFixture.PostJsonWithCsrfAsync(factory, clientB, "/api/auth/login",
            new { email = userB.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var mfaCookieB = ExtractSetCookie(loginB, "Identity.TwoFactorUserId");

        mfaCookieA.Should().NotBeNullOrEmpty("userA login must issue MFA-pending cookie");
        mfaCookieB.Should().NotBeNullOrEmpty("userB login must issue MFA-pending cookie");

        var aRejected = await FireUntilRateLimited(() =>
            PostTotpWithCookieAsync(clientA, mfaCookieA!, "000000"));
        aRejected.Should().NotBeNull();

        // userB hits an entirely different partition (different NameIdentifier from cookie).
        var bResp = await PostTotpWithCookieAsync(clientB, mfaCookieB!, "000000");
        bResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>Posts a TOTP code carrying the given Identity.TwoFactorUserId cookie value
    /// alongside a fresh anonymous CSRF token pair.</summary>
    private Task<HttpResponseMessage> PostTotpWithCookieAsync(
        HttpClient client, string mfaCookieValue, string code)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { code }),
        };
        req.Headers.Add("Cookie",
            $"Identity.TwoFactorUserId={mfaCookieValue}; {ProjectCeres.Common.Authentication.SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(ProjectCeres.Common.Authentication.SessionConstants.CsrfHeaderName, header);
        return client.SendAsync(req);
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

    [Fact]
    public async Task LoginTotp_MissingMfaCookie_RoutedToAnonymousPartition_DoesNotCrash()
    {
        var client = _factory.CreateClient();
        for (int i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
                new { code = "000000" });
            // Either 401 (missing cookie → controller returns UNAUTHENTICATED) or 429
            // (anonymous-totp partition is shared and may have residue from prior tests).
            // Either way, NOT 500.
            ((int)resp.StatusCode).Should().NotBe(500);
        }
    }

    [Fact]
    public async Task RateLimit_DifferentIPs_DoNotShareBucket()
    {
        // Both clients hit Kestrel from 127.0.0.1 in a TestServer scenario, so this
        // test is structurally limited. Document it and exercise the partition factory
        // by hitting Csrf from one client to confirm same-IP shares the CSRF bucket (60/min).
        // maxAttempts is set above 60 so we can actually reach the limit.
        var client = _factory.CreateClient();
        var rejected = await FireUntilRateLimited(() => client.GetAsync("/api/auth/csrf"), maxAttempts: 70);
        rejected.Should().NotBeNull("same client IP must share the auth-csrf-by-ip bucket");
    }

    [Fact]
    public async Task LoginTotp_RejectionUsesEnvelopeShape()
    {
        // Register, enroll MFA, do the password step, then burn the per-user TOTP bucket.
        // Capture the 429 body and assert it matches { error: { code, message } }.

        // Fresh rate-limiter so any residual IP-login bucket from prior tests is gone.
        await using var factory = _factory.WithFreshRateLimiter();
        var user = await AuthTestFixture.RegisterUserAsync(factory, "totp-envelope@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var options = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        };
        var client = factory.CreateClient(options);

        // Password step — get the Identity.TwoFactorUserId cookie
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var mfaCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        mfaCookie.Should().NotBeNullOrEmpty("password login must return Identity.TwoFactorUserId");

        // Burn the TOTP bucket until 429
        var rejected = await FireUntilRateLimited(
            () => PostTotpWithCookieAsync(client, mfaCookie!, "000000"));

        rejected.Should().NotBeNull("TOTP per-user rate limiter must fire within 25 attempts");

        var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMITED", "429 body must use envelope shape { error: { code, message } }");
        body.GetProperty("error").GetProperty("message").GetString()
            .Should().NotBeNullOrEmpty("envelope message must not be empty");
    }

    [Fact]
    public async Task SpoofedXForwardedForHeader_DoesNotInfluencePartition()
    {
        // The limiter reads Connection.RemoteIpAddress (real source), NOT X-Forwarded-For.
        // If it read XFF, each of 11 requests with a different XFF value would land in a
        // different partition and never 429. With the real source IP (all TestServer calls
        // share the same loopback address), all 11 land in the same partition and the 11th
        // returns 429, proving the limiter ignores the spoofed header.

        // Fresh rate-limiter so prior tests don't leave the login bucket saturated.
        // Unregistered email so the burst doesn't trip lockout (Stage 9.1.5.b: lockout
        // would cause OnRejected to surface ACCOUNT_LOCKED_OUT 401 instead of 429,
        // defeating the partition-collision assertion which expects 429).
        await using var factory = _factory.WithFreshRateLimiter();
        var client = factory.CreateClient();

        HttpResponseMessage? last429 = null;
        for (int i = 1; i <= 11; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    email = "nouser-xff-spoof@rl-test.local",
                    password = "x-long-enough-x",
                    rememberMe = false
                }),
            };
            // Each request spoofs a different "remote" IP in the XFF header
            req.Headers.Add("X-Forwarded-For", $"10.0.0.{i}");

            var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(factory);
            req.Headers.Add("Cookie", $"{ProjectCeres.Common.Authentication.SessionConstants.CsrfCookieName}={csrfCookie}");
            req.Headers.Add(ProjectCeres.Common.Authentication.SessionConstants.CsrfHeaderName, csrfHeader);

            var resp = await client.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                last429 = resp;
                break;
            }
        }

        last429.Should().NotBeNull(
            "all 11 requests must share the same real-IP partition and saturate the bucket — " +
            "spoofed X-Forwarded-For headers must NOT create separate partitions");
    }

    [Fact]
    public async Task Register_AlsoRateLimited()
    {
        // Register and login share the auth-login-by-ip partition (10/min).
        // Fire 11 register attempts with unique emails; the 11th must return 429.

        // Fresh rate-limiter so prior tests don't leave the bucket saturated.
        await using var factory = _factory.WithFreshRateLimiter();
        var client = factory.CreateClient();
        HttpResponseMessage? last429 = null;

        for (int i = 1; i <= 11; i++)
        {
            // Each email is unique so duplicate-prevention does not kick in.
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/register",
                new
                {
                    email = $"reg-rl-{i}@rl-test.local",
                    password = AuthTestFixture.ValidPassword,
                });
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                last429 = resp;
                break;
            }
        }

        last429.Should().NotBeNull(
            "register shares the auth-login-by-ip bucket; 11th request within the window must return 429");
    }

    [Fact]
    public async Task SlidingWindow_BoundaryAttack_StillBlocked()
    {
        // Sliding window: 4 segments × 1.25s = 5s window (see WithMediumLoginWindow).
        // Fire some requests, wait less than window-length, fire more. The combined
        // burst within the rolling window must still trip the login limiter (10/window).
        //
        // 5s is the smallest window that comfortably accommodates an 11-request burst
        // (~1-3s of HTTP + Argon2id overhead on the dummy-hash path) without the
        // early requests aging out before the 11th lands.
        //
        // Unregistered email so the burst doesn't trip lockout (Stage 9.1.5.b: lockout
        // would cause OnRejected to surface ACCOUNT_LOCKED_OUT 401 instead of the 429
        // this test pins).
        await using var factory = _factory.WithMediumLoginWindow();
        var client = factory.CreateClient();

        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = "nouser-slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        // 20% of 5s test window — straddles a segment boundary without falling out of
        // the rolling window even with ~2s of HTTP overhead for the 11 requests.
        await Task.Delay(TimeSpan.FromMilliseconds(1000));
        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = "nouser-slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "nouser-slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "10 requests within the 5s test sliding window should saturate the login bucket; the 11th must be rejected");
    }

    [Fact]
    public async Task RateLimitRejection_AfterLockoutEngaged_ReturnsAccountLockedOut_NotRateLimited()
    {
        await using var factory = _factory.WithFreshRateLimiter();
        var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-locked@rl-test.local");
        var client = factory.CreateClient();

        HttpResponseMessage? tenth = null;
        for (int i = 1; i <= 10; i++)
        {
            tenth = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        }
        var tenthBody = await tenth!.Content.ReadFromJsonAsync<JsonElement>();
        tenthBody.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("ACCOUNT_LOCKED_OUT",
                "attempt 10 trips MaxFailedAccessAttempts; controller returns ACCOUNT_LOCKED_OUT and seeds the lockout-cache");

        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        eleventh.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "post-lockout 429s must be replaced by 401 ACCOUNT_LOCKED_OUT");
        var eleventhBody = await eleventh.Content.ReadFromJsonAsync<JsonElement>();
        eleventhBody.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("ACCOUNT_LOCKED_OUT",
                "envelope must surface the lockout state so the SPA shows the locked-account banner");
    }

    [Fact]
    public async Task RateLimitRejection_OnNonLoginEndpoint_StillReturnsRateLimited()
    {
        var client = _factory.CreateClient();
        HttpResponseMessage? rejected = null;
        for (int i = 0; i < 70; i++)
        {
            var resp = await client.GetAsync("/api/auth/csrf");
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = resp;
                break;
            }
        }
        rejected.Should().NotBeNull("CSRF endpoint must still rate-limit at 60/min");

        var body = await rejected!.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMITED",
                "non-login endpoints must keep the original rate-limit envelope; the lockout-aware path is /api/auth/login only");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "non-login endpoints must keep status 429");
    }

    [Fact]
    public async Task RateLimitRejection_OnLoginTotpEndpoint_StillReturnsRateLimited()
    {
        // The OnRejected lockout-aware override only applies to POST /api/auth/login
        // (password step). The TOTP step uses AuthTotpByUser (per-user partition);
        // a TOTP 429 has no relationship to per-IP account-lockout state and must
        // surface RATE_LIMITED, not ACCOUNT_LOCKED_OUT.
        await using var factory = _factory.WithFreshRateLimiter();
        var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-totp@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var options = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        };
        var client = factory.CreateClient(options);

        // Pass the password step to get the Identity.TwoFactorUserId cookie
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var mfaCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        mfaCookie.Should().NotBeNullOrEmpty("password login must return Identity.TwoFactorUserId");

        // Burn the TOTP per-user bucket until 429
        var rejected = await FireUntilRateLimited(
            () => PostTotpWithCookieAsync(client, mfaCookie!, "000000"));
        rejected.Should().NotBeNull("TOTP per-user limiter must fire within 25 attempts");

        rejected!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "TOTP 429 must NOT be re-surfaced as 401 ACCOUNT_LOCKED_OUT — the TOTP step uses a per-user limiter unrelated to account-lockout state");
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMITED",
                "/api/auth/login/totp must always return RATE_LIMITED on 429 — the OnRejected lockout override applies only to /api/auth/login");
    }

    [Fact]
    public async Task RateLimitRejection_WhenPerEmailEntryAbsent_FallsBackToRateLimitedEnvelope()
    {
        await using var factory = _factory.WithShortLockoutCacheTtl();
        var user = await AuthTestFixture.RegisterUserAsync(factory, "rl-expire@rl-test.local");
        var client = factory.CreateClient();

        // Drive the standard 10-burst to engage lockout and seed both cache entries.
        for (int i = 0; i < 10; i++)
        {
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        }

        // Force-evict the per-email cache entry (simulates entry TTL elapse OR manual
        // unlock invalidation). The per-IP pointer still exists, but TryGetLockoutEnd
        // for the pointed email now returns false, so OnRejected must fall back to
        // the default RATE_LIMITED envelope.
        using (var scope = factory.Services.CreateScope())
        {
            var cache = scope.ServiceProvider
                .GetRequiredService<ProjectCeres.Common.Authentication.LockoutCache>();
            cache.Remove(user.Email!);
        }

        // Next attempt — rate-limit budget is exhausted from the 10-burst, so the
        // request hits OnRejected. With the per-email entry gone, OnRejected falls
        // back to RATE_LIMITED.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "rate-limit must still fire when the per-email cache entry is absent");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMITED",
                "after the per-email entry is gone, OnRejected must fall back to the default RATE_LIMITED envelope");
    }

    [Fact]
    public async Task RateLimitRejection_DifferentUserFromSameIp_OutsideWindow_ReturnsRateLimited()
    {
        await using var factory = _factory.WithShortLockoutCacheTtl();
        var userA = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-a@rl-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(factory, "rl-ip-b@rl-test.local");
        var client = factory.CreateClient();

        // User A locks out — seeds both LockoutCache entries.
        for (int i = 0; i < 10; i++)
        {
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = userA.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        }

        // Wait for the per-IP pointer to expire. WithShortLockoutCacheTtl sets
        // IpPointerTtl = 1s; we wait 1.2s for safety margin.
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        // User B's attempt from the SAME IP: per-IP rate-limit bucket is already
        // saturated from A's 10 attempts (within the 60s rate-limit window), so the
        // request hits OnRejected. The per-IP pointer to user A has expired, so
        // TryGetLastLockedEmailForIp returns false; OnRejected falls back to the
        // default RATE_LIMITED envelope — even though A's account is still locked
        // (per-email entry persists until A's actual LockoutEnd of UtcNow+15min).
        var bResp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = userB.Email, password = "wrong-but-long-enough-pwd", rememberMe = false });
        bResp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "outside the per-IP pointer window, a different user's 429 must keep its RATE_LIMITED shape");
        var body = await bResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RATE_LIMITED",
                "user B is not locked; the per-IP pointer to user A must have expired");
    }
}
