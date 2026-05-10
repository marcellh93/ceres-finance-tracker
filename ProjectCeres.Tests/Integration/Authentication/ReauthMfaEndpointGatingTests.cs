using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthMfaEndpointGatingTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;
    public ReauthMfaEndpointGatingTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpResponseMessage> HitWithStaleClaimAsync(
        ApplicationUser user, string url, object? body = null)
    {
        var stale = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, stale);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task MfaEnroll_now_requires_recent_auth()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-enr-{Guid.NewGuid():N}@example.com");
        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/enroll");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    [Fact]
    public async Task MfaEnrollVerify_now_requires_recent_auth()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-ver-{Guid.NewGuid():N}@example.com");
        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/enroll/verify", new { code = "123456" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    [Fact]
    public async Task MfaRegenerateBackupCodes_now_requires_recent_auth_not_in_body_totp()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"gate-rgn-{Guid.NewGuid():N}@example.com");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var resp = await HitWithStaleClaimAsync(user, "/api/auth/mfa/backup-codes/regenerate");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }
}
