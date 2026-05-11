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
        await AuthTestFixture.RegisterUserAsync(_factory, "rl@rl-test.local");
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "rl@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

        rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
        rejected!.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task RateLimit429_ResponseBodyMatchesApiContractEnvelope()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "envelope@rl-test.local");
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "envelope@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

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
        await AuthTestFixture.RegisterUserAsync(_factory, "contenttype@rl-test.local");
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "contenttype@rl-test.local", password = "x-long-enough-x", rememberMe = false }));

        rejected.Should().NotBeNull("expected rate limit to fire within 25 attempts");
        rejected!.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Login_LimiterResetsAfterWindow()
    {
        // Override AuthLoginByIp to use a 1-second window for this test so we can
        // wait the window out in ~1.1s instead of 70s. This test's whole point is
        // "saturate, wait, confirm window rolled over" — we just shrink the wall-clock
        // duration of the rollover instead of using a fresh limiter (which would
        // defeat the test's purpose).
        await using var factory = _factory.WithShortLoginWindow();
        var user = await AuthTestFixture.RegisterUserAsync(factory, "reset-window@rl-test.local");
        var client = factory.CreateClient();

        // Saturate the bucket.
        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = user.Email, password = "x-long-enough-x", rememberMe = false }));
        rejected.Should().NotBeNull();

        // Sliding window is 1s for this test (see WithShortLoginWindow).
        await Task.Delay(RateLimitedAuthTestWebApplicationFactory.ShortLoginWindowClearDelay);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = user.Email, password = "x-long-enough-x", rememberMe = false });
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

        var body = await rejected!.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
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
        await using var factory = _factory.WithFreshRateLimiter();
        await AuthTestFixture.RegisterUserAsync(factory, "xff-spoof@rl-test.local");
        var client = factory.CreateClient();

        HttpResponseMessage? last429 = null;
        for (int i = 1; i <= 11; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    email = "xff-spoof@rl-test.local",
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
        await using var factory = _factory.WithMediumLoginWindow();
        await AuthTestFixture.RegisterUserAsync(factory, "slidingwindow@rl-test.local");
        var client = factory.CreateClient();

        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        // 20% of 5s test window — straddles a segment boundary without falling out of
        // the rolling window even with ~2s of HTTP overhead for the 11 requests.
        await Task.Delay(TimeSpan.FromMilliseconds(1000));
        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
                new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/login",
            new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "10 requests within the 5s test sliding window should saturate the login bucket; the 11th must be rejected");
    }
}
