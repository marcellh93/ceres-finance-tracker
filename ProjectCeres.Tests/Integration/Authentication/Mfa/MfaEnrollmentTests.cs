using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationTests")]
public class MfaEnrollmentTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public MfaEnrollmentTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@mfa-enroll-test.local")).ToList())
        {
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Enroll_returns_otpauth_uri_and_manual_entry_key()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "e@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var sessionCookie = await LoginAndGetSessionCookieAsync(client, "e@mfa-enroll-test.local");

        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var resp = await client.SendAsync(enrollReq);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("otpAuthUri").GetString().Should()
            .StartWith("otpauth://totp/Project%20Ceres:e%40mfa-enroll-test.local?secret=");
        body.GetProperty("manualEntryKey").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Enroll_response_carries_no_store_cache_control()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "c@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await LoginAndGetSessionCookieAsync(client, "c@mfa-enroll-test.local");

        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var resp = await client.SendAsync(enrollReq);

        resp.Headers.CacheControl!.NoStore.Should().BeTrue();
        resp.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task EnrollVerify_with_correct_code_flips_TwoFactorEnabled_and_returns_10_backup_codes()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "v@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await LoginAndGetSessionCookieAsync(client, "v@mfa-enroll-test.local");

        // Enroll → get the seed by reading the otpAuthUri's secret param
        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        var enrollResp = await client.SendAsync(enrollReq);
        var enrollBody = await enrollResp.Content.ReadFromJsonAsync<JsonElement>();
        var otpAuthUri = enrollBody.GetProperty("otpAuthUri").GetString()!;
        var seed = ExtractSecretFromUri(otpAuthUri);

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        var (verifyCookie, verifyHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
        {
            Content = JsonContent.Create(new { code }),
        };
        verifyReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={verifyCookie}");
        verifyReq.Headers.Add(SessionConstants.CsrfHeaderName, verifyHeader);
        var resp = await client.SendAsync(verifyReq);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var codes = body.GetProperty("backupCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
        codes.Should().HaveCount(10);
        codes.Should().OnlyHaveUniqueItems();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await userManager.FindByIdAsync(user.Id.ToString());
        refreshed!.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task EnrollVerify_with_wrong_code_keeps_TwoFactorEnabled_false()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "w@mfa-enroll-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await LoginAndGetSessionCookieAsync(client, "w@mfa-enroll-test.local");

        var (enrollCookie, enrollHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollHeader);
        await client.SendAsync(enrollReq);

        var (verifyCookie, verifyHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var verifyReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll/verify")
        {
            Content = JsonContent.Create(new { code = "000000" }),
        };
        verifyReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={verifyCookie}");
        verifyReq.Headers.Add(SessionConstants.CsrfHeaderName, verifyHeader);
        var resp = await client.SendAsync(verifyReq);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await userManager.FindByIdAsync(user.Id.ToString());
        refreshed!.TwoFactorEnabled.Should().BeFalse();
    }

    private async Task<string?> LoginAndGetSessionCookieAsync(HttpClient client, string email)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        return ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);
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

    private static string ExtractSecretFromUri(string otpAuthUri)
    {
        var uri = new Uri(otpAuthUri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["secret"]!;
    }
}
