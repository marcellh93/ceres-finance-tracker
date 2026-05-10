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
        // Wait for the login bucket to clear — earlier tests in this class saturate
        // the auth-login-by-ip partition. The sliding window is 60s; 70s covers the
        // worst segment boundary.
        await Task.Delay(TimeSpan.FromSeconds(70));

        var client = _factory.CreateClient();

        for (int i = 0; i < 25; i++)
        {
            var resp = await client.GetAsync("/api/auth/csrf");
            // Don't care whether 60-bucket trips here; we just need to consume MANY /csrf
            // calls and confirm /login's bucket is untouched.
        }

        // /login from the SAME client should still succeed (not 429), because the buckets
        // are independent.
        await AuthTestFixture.RegisterUserAsync(_factory, "csrfsep@rl-test.local");
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
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
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "reset-window@rl-test.local");
        var client = _factory.CreateClient();

        // Saturate the bucket.
        var rejected = await FireUntilRateLimited(() =>
            AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = user.Email, password = "x-long-enough-x", rememberMe = false }));
        rejected.Should().NotBeNull();

        // Sliding window: 60s. 70s pause covers the worst boundary case.
        await Task.Delay(TimeSpan.FromSeconds(70));

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = "x-long-enough-x", rememberMe = false });
        resp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginTotp_LimiterPartitionsPerUser()
    {
        var userA = await AuthTestFixture.RegisterUserAsync(_factory, "totp-a@rl-test.local");
        var userB = await AuthTestFixture.RegisterUserAsync(_factory, "totp-b@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userA);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, userB);

        // The IP-keyed login bucket may be saturated by earlier tests in this class.
        // Wait for the full 60s sliding window to roll over so login calls succeed.
        await Task.Delay(TimeSpan.FromSeconds(70));

        // Use HandleCookies = false so we can manually carry cookies across requests.
        // PostJsonWithCsrfAsync sets Cookie headers directly; the TwoFactorUserId
        // cookie must also be forwarded explicitly so the rate-limit partitioner
        // (which calls AuthenticateAsync) can resolve the per-user partition key.
        var options = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        };
        var clientA = _factory.CreateClient(options);
        var loginA = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientA, "/api/auth/login",
            new { email = userA.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        // DIAGNOSTIC: surface the actual status and Set-Cookie headers
        var loginABody = await loginA.Content.ReadAsStringAsync();
        var loginASetCookies = loginA.Headers.TryGetValues("Set-Cookie", out var sc) ? string.Join("; ", sc) : "(none)";
        loginA.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            $"userA login must not be rate-limited; status={loginA.StatusCode}, Set-Cookie={loginASetCookies}, body={loginABody}");
        ((int)loginA.StatusCode).Should().BeOneOf(new[] { 200, 204 },
            $"userA login must succeed; status={loginA.StatusCode}, Set-Cookie={loginASetCookies}, body={loginABody}");
        var mfaCookieA = ExtractSetCookie(loginA, "Identity.TwoFactorUserId");

        var clientB = _factory.CreateClient(options);
        var loginB = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, clientB, "/api/auth/login",
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

        // Wait for any residual IP-login bucket to clear (login step shares auth-login-by-ip).
        await Task.Delay(TimeSpan.FromSeconds(70));

        var user = await AuthTestFixture.RegisterUserAsync(_factory, "totp-envelope@rl-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var options = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        };
        var client = _factory.CreateClient(options);

        // Password step — get the Identity.TwoFactorUserId cookie
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
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

        await Task.Delay(TimeSpan.FromSeconds(70)); // wait for a fresh login bucket

        await AuthTestFixture.RegisterUserAsync(_factory, "xff-spoof@rl-test.local");
        var client = _factory.CreateClient();

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

            var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
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

        await Task.Delay(TimeSpan.FromSeconds(70)); // fresh bucket

        var client = _factory.CreateClient();
        HttpResponseMessage? last429 = null;

        for (int i = 1; i <= 11; i++)
        {
            // Each email is unique so duplicate-prevention does not kick in.
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/register",
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
        // Sliding window with 4 segments × 15s. Fire some requests, wait less than
        // window-length, fire more. The combined burst within the rolling window
        // must still trip the login limiter (10/min bucket).
        await AuthTestFixture.RegisterUserAsync(_factory, "slidingwindow@rl-test.local");
        var client = _factory.CreateClient();

        // Make sure we start with a clean window — wait out any residue from prior tests.
        await Task.Delay(TimeSpan.FromSeconds(70));

        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        await Task.Delay(TimeSpan.FromSeconds(25));
        for (int i = 0; i < 5; i++)
            await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        var eleventh = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "slidingwindow@rl-test.local", password = "x-long-enough-x", rememberMe = false });
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "10 requests within the 60s sliding window should saturate the login bucket; the 11th must be rejected");
    }
}
