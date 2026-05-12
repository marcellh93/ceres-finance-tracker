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

[Collection("IntegrationTests")]
public class LoginScopedCookieTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LoginScopedCookieTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@scoped-cookie-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Identity_TwoFactorUserId_cookie_does_not_grant_access_to_authenticated_endpoints()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "s@scoped-cookie-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step A — credentials login → only the scoped cookie is set
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "s@scoped-cookie-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        // Step B — try to access an authenticated endpoint with ONLY the scoped cookie.
        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        probe.Headers.Add("Cookie", $"Identity.TwoFactorUserId={twoFactorCookie}");
        var resp = await client.SendAsync(probe);

        // The scoped cookie is NOT a session — global fallback policy returns 401.
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string? ExtractSetCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var first = v.Split(';')[0];
            var eq = first.IndexOf('=');
            if (eq > 0 && first[..eq].Trim() == cookieName) return first[(eq + 1)..];
        }
        return null;
    }
}
