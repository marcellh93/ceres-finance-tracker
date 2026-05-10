using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthCrossFeatureTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;
    public ReauthCrossFeatureTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Existing_login_flow_still_works_with_LastReauthAt_added()
    {
        var email = $"xf-login-{Guid.NewGuid():N}@example.com";
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Session cookie issued and contains the LastReauthAt claim — implicit; the smoke is
        // "login still 204s and a session cookie is present."
        resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
    }

    [Fact]
    public async Task RefreshSignInAsync_after_reauth_does_not_disrupt_persistent_cookie()
    {
        var email = $"xf-pers-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = true }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
            .Split(';')[0]
            .Substring(SessionConstants.SessionCookieName.Length + 1);
        var persistentCookieBefore = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="));

        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var reauthReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        reauthReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        reauthReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var reauthResp = await client.SendAsync(reauthReq);
        reauthResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The reauth response should NOT include a Set-Cookie for __Host-Persist;
        // RefreshSignInAsync only re-issues __Host-Session.
        reauthResp.Headers.TryGetValues("Set-Cookie", out var setCookies);
        if (setCookies is not null)
        {
            setCookies.Should().NotContain(c => c.StartsWith($"{SessionConstants.PersistentCookieName}="),
                "RefreshSignInAsync only re-issues the application cookie; persistent cookie should be untouched");
        }
    }

    [Fact]
    public async Task Reauth_when_user_is_deleted_returns_401_UNAUTHENTICATED()
    {
        var email = $"xf-del-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            await um.DeleteAsync(fresh!);
        }

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        // SecurityStampValidator may reject the cookie before our handler runs.
        // Either way, the response is 401 (with or without an envelope).
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
