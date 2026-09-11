using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationParallel2")]
public class LoginWithTotpTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public LoginWithTotpTests(AuthTestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@mfa-login-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_with_mfa_enabled_returns_200_requiresTotp_and_no_session_cookie()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "m@mfa-login-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "m@mfa-login-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("requiresTotp").GetBoolean().Should().BeTrue();

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        setCookies.Should().NotContain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));
        setCookies.Should().Contain(c => c.StartsWith("Identity.TwoFactorUserId="));
    }

    [Fact]
    public async Task LoginTotp_with_valid_totp_issues_session_cookie_and_inserts_user_session()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "t@mfa-login-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (twoFactorCookie, mfaRememberMeCookie) = await DoCredentialsLoginAsync(client, "t@mfa-login-test.local");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await SubmitTotpAsync(client, twoFactorCookie!, mfaRememberMeCookie, user.Id, code);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        resp.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions.IgnoreQueryFilters().FirstAsync(s => s.UserId == user.Id);
        session.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task LoginTotp_with_replayed_code_returns_401()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "p@mfa-login-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (twoFactorCookie1, mfaRm1) = await DoCredentialsLoginAsync(client, "p@mfa-login-test.local");
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var first = await SubmitTotpAsync(client, twoFactorCookie1!, mfaRm1, user.Id, code);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (twoFactorCookie2, mfaRm2) = await DoCredentialsLoginAsync(client, "p@mfa-login-test.local");
        var resp = await SubmitTotpAsync(client, twoFactorCookie2!, mfaRm2, user.Id, code);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginTotp_with_valid_backup_code_issues_session_and_marks_used()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "b@mfa-login-test.local");
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Generate backup codes directly via the service.
        string firstCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await svc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
            firstCode = codes.First();
        }

        var (twoFactorCookie, mfaRm) = await DoCredentialsLoginAsync(client, "b@mfa-login-test.local");
        var resp = await SubmitTotpAsync(client, twoFactorCookie!, mfaRm, user.Id, firstCode);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var used = await db.UserMfaBackupCodes
            .IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id && c.UsedAt != null)
            .CountAsync();
        used.Should().Be(1);
    }

    private async Task<(string?, string?)> DoCredentialsLoginAsync(HttpClient client, string email)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        return (ExtractSetCookie(resp, "Identity.TwoFactorUserId"),
                ExtractSetCookie(resp, "Mfa.RememberMe"));
    }

    private async Task<HttpResponseMessage> SubmitTotpAsync(
        HttpClient client, string twoFactorCookie, string? mfaRememberMeCookie, Guid userId, string code)
    {
        // Anonymous CSRF: the user is half-authenticated (Identity.TwoFactorUserId only),
        // not signed-in, so the antiforgery token must be anonymous-bound.
        var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code }),
        };
        var cookieValue = $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={totpCsrf}";
        if (!string.IsNullOrEmpty(mfaRememberMeCookie))
        {
            cookieValue += $"; Mfa.RememberMe={mfaRememberMeCookie}";
        }
        totpReq.Headers.Add("Cookie", cookieValue);
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
        return await client.SendAsync(totpReq);
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
