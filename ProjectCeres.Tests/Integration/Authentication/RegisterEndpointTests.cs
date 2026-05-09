using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class RegisterEndpointTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@register-test.local")).ToList())
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
    public async Task Register_rejects_duplicate_email()
    {
        await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email = "dup@register-test.local",
            password = "correct horse battery staple"
        });
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email = "dup@register-test.local",
            password = "correct horse battery staple"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
