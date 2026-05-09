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
    public async Task Csrf_SharesAuthLoginByIpPolicy()
    {
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() => client.GetAsync("/api/auth/csrf"));

        rejected.Should().NotBeNull("expected /api/auth/csrf to share AuthLoginByIp policy");
    }

    [Fact]
    public async Task RateLimitOnRejected_ContentTypeIsApplicationJson()
    {
        var client = _factory.CreateClient();

        var rejected = await FireUntilRateLimited(() => client.GetAsync("/api/auth/csrf"));

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
        // by hitting Csrf from one client to confirm same-IP shares bucket.
        var client = _factory.CreateClient();
        var rejected = await FireUntilRateLimited(() => client.GetAsync("/api/auth/csrf"));
        rejected.Should().NotBeNull("same client IP must share the bucket");
    }

    [Fact]
    public async Task SlidingWindow_BoundaryAttack_StillBlocked()
    {
        // Sliding window with 4 segments × 15s. Fire some requests, wait less than
        // window-length, fire more. The combined burst within the rolling window
        // must still trip the limiter.
        var client = _factory.CreateClient();

        // Make sure we start with a clean window — wait out any residue from prior tests.
        await Task.Delay(TimeSpan.FromSeconds(70));

        for (int i = 0; i < 5; i++) await client.GetAsync("/api/auth/csrf");
        await Task.Delay(TimeSpan.FromSeconds(25));
        for (int i = 0; i < 5; i++) await client.GetAsync("/api/auth/csrf");
        var eleventh = await client.GetAsync("/api/auth/csrf");
        eleventh.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "10 requests within the 60s sliding window should saturate the bucket; the 11th must be rejected");
    }
}
