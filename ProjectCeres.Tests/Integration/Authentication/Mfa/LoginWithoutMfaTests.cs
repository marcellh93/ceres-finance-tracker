using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationParallel3")]
public class LoginWithoutMfaTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;

    public LoginWithoutMfaTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@no-mfa-login-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_without_mfa_returns_204_and_session_cookie_regardless_of_account_age()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "n@no-mfa-login-test.local");

        // Backdate CreatedAt to 30 days ago to confirm there's no grace cliff.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Users.FirstAsync(u => u.Id == user.Id);
            stored.CreatedAt = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "n@no-mfa-login-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.GetValues("Set-Cookie").Should()
            .Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
    }
}
