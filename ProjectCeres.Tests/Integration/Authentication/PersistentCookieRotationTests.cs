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

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class PersistentCookieRotationTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PersistentCookieRotationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@persist-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Persistent_token_is_rotated_on_use_and_old_token_is_rejected()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "p@persist-test.local");

        // Step A — login with rememberMe; capture __Host-Persist value.
        var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "p@persist-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = true
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await loginClient.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var oldPersist = ExtractCookie(loginResp.Headers.GetValues("Set-Cookie"), "__Host-Persist");
        oldPersist.Should().NotBeNull();

        // Step B — clear the session cookie, send only the persistent cookie. Handler rotates.
        var rotateClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var rotateReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        rotateReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var rotateResp = await rotateClient.SendAsync(rotateReq);
        var rotateSetCookies = rotateResp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        var newPersist = ExtractCookie(rotateSetCookies, "__Host-Persist");
        newPersist.Should().NotBeNull();
        newPersist.Should().NotBe(oldPersist);

        // Step C — replay the OLD token; should not authenticate.
        var replayClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var replayReq = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        replayReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}={oldPersist}");
        var replay = await replayClient.SendAsync(replayReq);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string? ExtractCookie(IEnumerable<string> setCookies, string name)
    {
        foreach (var c in setCookies)
        {
            var first = c.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == name) return first[(eq + 1)..];
        }
        return null;
    }
}
