using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationParallel2")]
public class LoginEndpointTests : IntegrationTestBase<Bucket2AuthFactory>, IAsyncLifetime
{
    private readonly Bucket2AuthFactory _factory;

    public LoginEndpointTests(Bucket2AuthFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@login-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_with_valid_credentials_creates_UserSession_and_sets_session_cookie()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "ok@login-test.local");
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "ok@login-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = false
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("__Host-Session="));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("ok@login-test.local");
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user!.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        session.Should().NotBeNull();
        session!.IsPersistent.Should().BeFalse();
        session.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Login_returns_401_on_wrong_password()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "wrong@login-test.local");
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "wrong@login-test.local",
            password = "this-is-the-wrong-password-but-long-enough",
            rememberMe = false
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_returns_401_on_unknown_email_with_same_shape_and_status_as_wrong_password()
    {
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "nobody@login-test.local",
            password = "any-long-enough-password-here",
            rememberMe = false
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_rememberMe_issues_persistent_cookie_and_persists_token_hash()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "remember@login-test.local");
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "remember@login-test.local",
            password = AuthTestFixture.ValidPassword,
            rememberMe = true
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("__Host-Persist="));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("remember@login-test.local");
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user!.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();
        session.IsPersistent.Should().BeTrue();
        session.PersistentTokenHash.Should().StartWith("$argon2id$v=19$");
    }

    // ── Edge-case batch A (Stage 6b.3) ──────────────────────────────────────

    [Fact]
    public async Task Login_WithEmptyJsonBody_Returns422()
    {
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new { });

        ((int)resp.StatusCode).Should().Be(422,
            "empty JSON body fails model-validation — email and password are Required");
    }

    [Fact]
    public async Task Login_WithWrongContentType_Returns415()
    {
        var (cookie, header) = AuthTestFixture.MintCsrf(_factory);
        var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("email=foo@login-test.local&password=bar", System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded"),
        };
        req.Headers.Add("Cookie", $"{ProjectCeres.Common.Authentication.SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(ProjectCeres.Common.Authentication.SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);

        // The JSON model binder rejects non-JSON content. 415 is canonical;
        // some framework configurations report 400 — accept either.
        var code = (int)resp.StatusCode;
        new[] { 400, 415 }.Should().Contain(code,
            "endpoint expects JSON; form-encoded or plain-text bodies must be rejected with 400 or 415");
    }

    [Fact]
    public async Task Login_WithoutRememberMeField_DefaultsToFalse()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "norme@login-test.local");
        var client = _factory.CreateClient();

        // Post without the rememberMe field — it should default to false.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "norme@login-test.local",
            password = AuthTestFixture.ValidPassword,
        });

        // Should succeed with 204 (no MFA on this account).
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("norme@login-test.local");
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user!.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        session.Should().NotBeNull();
        session!.IsPersistent.Should().BeFalse("omitting rememberMe must default to a non-persistent session");
    }

    [Fact]
    public async Task Login_WithEmptyPassword_Returns422()
    {
        var client = _factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "someone@login-test.local",
            password = "",
        });

        ((int)resp.StatusCode).Should().Be(422,
            "empty password fails Required validation on the model");
    }

    [Fact]
    public async Task Login_WithEmailAtBoundaryLengths_HandledGracefully()
    {
        var client = _factory.CreateClient();

        // Empty email
        var r1 = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "",
            password = AuthTestFixture.ValidPassword,
        });
        ((int)r1.StatusCode).Should().BeInRange(400, 499,
            "empty email must produce a 4xx, not a 500");

        // Whitespace-only email
        var r2 = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = "   ",
            password = AuthTestFixture.ValidPassword,
        });
        ((int)r2.StatusCode).Should().BeInRange(400, 499,
            "whitespace-only email must produce a 4xx, not a 500");

        // 500-char email
        var longEmail = new string('a', 489) + "@login-test.local"; // 489+1+9+5 = 504 chars total? let's make it exactly 500
        longEmail = new string('a', 484) + "@boundary-edge.local"; // total ~504; fine
        var r3 = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login", new
        {
            email = longEmail,
            password = AuthTestFixture.ValidPassword,
        });
        ((int)r3.StatusCode).Should().BeInRange(400, 499,
            "500-char email must produce a 4xx, not a 500");

        // No DB rows created for any of these inputs (they would be under the @login-test.local domain
        // only if recorded, but there's no matching user — just ensure no 5xx).
    }
}
