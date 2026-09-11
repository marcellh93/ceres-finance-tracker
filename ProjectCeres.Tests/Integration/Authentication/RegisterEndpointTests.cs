using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel3")]
public class RegisterEndpointTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;
    private readonly HttpClient _client;

    public RegisterEndpointTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u =>
            u.Email!.EndsWith("@register-test.local") ||
            u.Email!.EndsWith("@register-noenum-test.local")).ToList())
        {
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Register_returns_204_and_persists_user_with_argon2id_hash()
    {
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email = "ok@register-test.local",
            password = "correct horse battery staple"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("ok@register-test.local");
        user.Should().NotBeNull();
        user!.PasswordHash.Should().StartWith("$argon2id$v=19$m=19456,t=2,p=1$");
    }

    [Fact]
    public async Task Register_rejects_password_below_pre_mfa_minimum_length()
    {
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email = "short@register-test.local",
            password = "abc12345"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_rejects_breached_password()
    {
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email = "breached@register-test.local",
            password = "Password1!"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Edge-case batch A (Stage 6b.3) ──────────────────────────────────────

    [Fact]
    public async Task Register_NoBody_Returns400()
    {
        // POST with no body at all (Content-Length: 0, no Content-Type).
        // The JSON binder cannot construct the model — expect 400 or 422.
        var (cookie, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        req.Headers.Add("Cookie", $"{ProjectCeres.Common.Authentication.SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(ProjectCeres.Common.Authentication.SessionConstants.CsrfHeaderName, header);

        var resp = await _client.SendAsync(req);

        // Framework returns 415 when no Content-Type is set (no JSON binder match),
        // 400/422 when body is present but invalid. All are acceptable — the key
        // contract is that the server does NOT return 500.
        ((int)resp.StatusCode).Should().BeOneOf([400, 415, 422],
            "missing body must be rejected gracefully — not a 500");
    }

    // Registration must commit AspNetUsers + category seed + audit-log entry atomically.
    // If category seeding fails, the AspNetUsers row must NOT remain — otherwise the
    // email is permanently un-registrable (re-register would hit DuplicateUserName and
    // return a silent 204 per anti-enumeration), and the user would be locked out from
    // ever using the application with that email.
    [Fact]
    public async Task Register_rolls_back_user_row_when_category_seeding_fails()
    {
        await using var factory = _factory
            .WithReplacedScopedService<CategorySeedService, ThrowingCategorySeedService>();
        var client = factory.CreateClient();

        var email = "atomic@register-test.local";
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(factory, client, "/api/auth/register", new
        {
            email,
            password = "correct horse battery staple"
        });

        // The contract is: when the post-CreateAsync work fails, the endpoint must NOT
        // report success. 500 is acceptable here (the seed failure is a server error);
        // the critical assertion is the persistence-state check below.
        ((int)resp.StatusCode).Should().NotBe((int)HttpStatusCode.NoContent,
            "registration must not silently succeed if category seeding fails");

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var orphan = await userManager.FindByEmailAsync(email);
        orphan.Should().BeNull(
            "AspNetUsers row must be rolled back when the post-create transaction fails; otherwise the email is permanently un-registrable");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsSameShapeAsNewEmail()
    {
        // Register a fresh email — must succeed with 204.
        var firstResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = "first@register-noenum-test.local", password = "correct horse battery staple" });
        firstResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var firstBody = await firstResp.Content.ReadAsStringAsync();

        // Re-register the SAME email — must return the SAME shape (204 + identical body) so the
        // attacker can't distinguish "exists" from "doesn't exist".
        var dupResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = "first@register-noenum-test.local", password = "correct horse battery staple" });
        dupResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dupBody = await dupResp.Content.ReadAsStringAsync();

        dupBody.Should().Be(firstBody, "duplicate-email registration must return identical body shape to a fresh-email registration (no user enumeration)");

        // Sanity: a different fresh email also succeeds with the same shape.
        var thirdResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = "second@register-noenum-test.local", password = "correct horse battery staple" });
        thirdResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var thirdBody = await thirdResp.Content.ReadAsStringAsync();
        thirdBody.Should().Be(firstBody);
    }
}
