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

[Collection("IntegrationParallel4")]
public class MfaEnrollmentTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;

    public MfaEnrollmentTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

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
            .StartWith("otpauth://totp/Ceres:e%40mfa-enroll-test.local?secret=");
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

    [Fact]
    public async Task Enroll_WhenAlreadyEnrolled_Returns409_AndAuthenticatorKeyUnchanged()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "already@mfa-enroll-test.local");
        var originalSeed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Full two-step login for an MFA-enabled user (HandleCookies = false — matches existing pattern).
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1 — credentials → get Identity.TwoFactorUserId cookie
        var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf1}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header1);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty("credentials login must return Identity.TwoFactorUserId");

        // Step 2 — TOTP login → get session cookie
        var loginCode = AuthTestFixture.ComputeCurrentTotpCode(originalSeed);
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = loginCode }),
        };
        totpReq.Headers.Add("Cookie", $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var totpResp = await client.SendAsync(totpReq);
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent, "TOTP login must succeed");
        var sessionCookie = ExtractSetCookie(totpResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty("TOTP login must issue a session cookie");

        // Attempt to re-enroll — must return 409, not rotate the key.
        var (enrollCsrfCookie, enrollCsrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var enrollReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        enrollReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={enrollCsrfCookie}");
        enrollReq.Headers.Add(SessionConstants.CsrfHeaderName, enrollCsrfHeader);
        var resp = await client.SendAsync(enrollReq);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("MFA_ALREADY_ENROLLED");

        // The seed must NOT have been rotated.
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByIdAsync(user.Id.ToString());
        var currentSeed = await um.GetAuthenticatorKeyAsync(fresh!);
        currentSeed.Should().Be(originalSeed, "re-enroll must NOT rotate AuthenticatorKey when MFA is already enabled");
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
