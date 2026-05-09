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
}
