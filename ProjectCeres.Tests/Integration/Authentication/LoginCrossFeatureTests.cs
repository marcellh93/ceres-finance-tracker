using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("RateLimitTests")]
public class LoginCrossFeatureTests : IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public LoginCrossFeatureTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@cross-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task RateLimitFires_BeforeAntiforgeryValidation()
    {
        var client = _factory.CreateClient();
        // Saturate the bucket with valid CSRF.
        await AuthTestFixture.RegisterUserAsync(_factory, "csrf@cross-test.local");
        for (int i = 0; i < 30; i++)
        {
            var r = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "csrf@cross-test.local", password = "wrong-but-long-enough", rememberMe = false });
            if (r.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        // Now submit WITHOUT CSRF header. If antiforgery ran first, we'd see 400.
        // Rate limit must intercept first → 429.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "csrf@cross-test.local", password = "x-long-enough", rememberMe = false }),
        };
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

[Collection("IntegrationTests")]
public class LoginCrossFeatureRegressionTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public LoginCrossFeatureRegressionTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@regress-test.local")).ToList())
            await um.DeleteAsync(u);
    }

    [Fact]
    public async Task LoginWithoutMfaEnrolled_StillSucceeds()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "no-mfa@regress-test.local");
        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LoginWithMfaEnrolled_StillSucceeds_HappyPath()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "with-mfa@regress-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login/totp",
            new { code = AuthTestFixture.ComputeCurrentTotpCode(seed) });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BadCredentials_AlwaysReturnsArgon2idTimingFloor()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "timing@regress-test.local");
        var client = _factory.CreateClient();

        var sw1 = Stopwatch.StartNew();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "timing@regress-test.local", password = "wrong-but-long-enough", rememberMe = false });
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = "ghost@regress-test.local", password = "wrong-but-long-enough", rememberMe = false });
        sw2.Stop();

        // Both must run Argon2id; allow generous ±200ms tolerance for CI noise.
        var diff = Math.Abs(sw1.ElapsedMilliseconds - sw2.ElapsedMilliseconds);
        diff.Should().BeLessThan(200);
    }
}
