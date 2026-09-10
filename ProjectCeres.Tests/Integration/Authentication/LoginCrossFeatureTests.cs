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
        // Saturate the bucket with valid CSRF. Use an UNREGISTERED email so the
        // burst doesn't trip lockout (Stage 9.1.5.b: lockout would cause OnRejected
        // to surface ACCOUNT_LOCKED_OUT 401 instead of the 429 this test pins).
        for (int i = 0; i < 30; i++)
        {
            var r = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
                new { email = "nouser-csrf@cross-test.local", password = "wrong-but-long-enough", rememberMe = false });
            if (r.StatusCode == HttpStatusCode.TooManyRequests) break;
        }

        // Now submit WITHOUT CSRF header. If antiforgery ran first, we'd see 400.
        // Rate limit must intercept first → 429.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nouser-csrf@cross-test.local", password = "x-long-enough", rememberMe = false }),
        };
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

[Collection("IntegrationParallel3")]
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
    public async Task BadCredentials_known_user_and_unknown_email_perform_same_Argon2id_count()
    {
        // Production audit (2026-05-12): the known-bad-password branch runs ONE Argon2id
        // (via PasswordSignInAsync → VerifyHashedPassword); Identity's AccessFailedAsync
        // does NOT verify again — it just increments AccessFailedCount. The unknown-email
        // branch runs ONE Argon2id (via RunDummyHash). Count-equality is the security
        // property the constant-time defence claims to guarantee; we assert it directly
        // rather than measuring wall-clock duration (the latter was flake-prone under
        // integration-suite CPU contention and went through three threshold tunings
        // before this rewrite).
        await using var factory = _factory.WithArgon2idCounter(out var counter);
        var client = factory.CreateClient();

        // Warm up JIT for the counting hasher path.
        var warmupUser = await AuthTestFixture.RegisterUserAsync(_factory, $"warm-count-{Guid.NewGuid():N}@regress-test.local");
        await PostBadLoginAsync(client, warmupUser.Email!);
        await PostBadLoginAsync(client, $"ghost-warm-{Guid.NewGuid():N}@regress-test.local");

        var knownUser = await AuthTestFixture.RegisterUserAsync(_factory, $"known-count-{Guid.NewGuid():N}@regress-test.local");

        // Known-user-bad-password branch.
        counter.Reset();
        await PostBadLoginAsync(client, knownUser.Email!);
        var knownCount = counter.Count;

        // Unknown-email branch.
        counter.Reset();
        await PostBadLoginAsync(client, $"ghost-{Guid.NewGuid():N}@regress-test.local");
        var unknownCount = counter.Count;

        unknownCount.Should().Be(knownCount,
            "constant-time defence: both branches must perform the same number of " +
            "Argon2id operations. known={0}, unknown={1}. If this trips, audit whether " +
            "AccessFailedAsync started running a second Argon2id internally, or whether " +
            "RunDummyHash was removed from the unknown branch.",
            knownCount, unknownCount);

        knownCount.Should().BeGreaterThan(0,
            "both branches must perform at least one Argon2id (verify + dummy)");
    }

    private Task<HttpResponseMessage> PostBadLoginAsync(HttpClient client, string email) =>
        AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email, password = "wrong-but-long-enough", rememberMe = false });
}
