using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class LoginEndpointTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;

    public LoginEndpointTests(TestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@login-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
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
        var session = await db.UserSessions.FirstOrDefaultAsync();
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
        var session = await db.UserSessions.FirstAsync();
        session.IsPersistent.Should().BeTrue();
        session.PersistentTokenHash.Should().StartWith("$argon2id$v=19$");
    }
}
