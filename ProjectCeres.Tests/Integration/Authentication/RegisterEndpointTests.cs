using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class RegisterEndpointTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterEndpointTests(AuthTestWebApplicationFactory factory)
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
