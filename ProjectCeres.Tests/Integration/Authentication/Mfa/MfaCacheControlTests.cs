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

[Collection("IntegrationParallel4")]
public class MfaCacheControlTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;

    public MfaCacheControlTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@cache-mfa-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Enroll_verify_and_regenerate_all_set_no_store()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "h@cache-mfa-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await LoginAndGetSessionCookieAsync(client, "h@cache-mfa-test.local");

        // /enroll
        var enroll = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/enroll", body: null);
        enroll.Headers.CacheControl!.NoStore.Should().BeTrue();

        // /enroll/verify (read seed from /enroll body, compute current code, verify)
        var enrollBody = await enroll.Content.ReadFromJsonAsync<JsonElement>();
        var seed = ExtractSecretFromUri(enrollBody.GetProperty("otpAuthUri").GetString()!);
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        var verify = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/enroll/verify",
            new { code });
        verify.Headers.CacheControl!.NoStore.Should().BeTrue();

        // /backup-codes/regenerate — no body required after Task 11; gate is [RequireRecentAuth]
        // (LastReauthAt is stamped at login, so the request passes immediately).
        var regen = await PostMfaAsync(client, sessionCookie!, user.Id, "/api/auth/mfa/backup-codes/regenerate",
            body: null);
        regen.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private async Task<string?> LoginAndGetSessionCookieAsync(HttpClient client, string email)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var resp = await client.SendAsync(req);
        return ExtractSetCookie(resp, SessionConstants.SessionCookieName);
    }

    private async Task<HttpResponseMessage> PostMfaAsync(
        HttpClient client, string sessionCookie, Guid userId, string path, object? body)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
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
