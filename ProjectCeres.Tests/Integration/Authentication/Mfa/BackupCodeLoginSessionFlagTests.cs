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

/// <summary>
/// Stage 9.7 — pins the UsedBackupCodeAtLogin contract on UserSession and the
/// matching UsedBackupCodeAtLastLogin field on the MeResponse DTO. The flag
/// drives the dashboard backup-code banner.
/// </summary>
[Collection("IntegrationParallel3")]
public class BackupCodeLoginSessionFlagTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private const string EmailSuffix = "@backup-flag-test.local";

    private readonly Bucket3AuthFactory _factory;

    public BackupCodeLoginSessionFlagTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Login_with_backup_code_sets_UsedBackupCodeAtLogin_true_on_session_row()
    {
        const string email = "backup-1" + EmailSuffix;
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        string firstBackupCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await svc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
            firstBackupCode = codes.First();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (twoFactorCookie, mfaRm) = await DoCredentialsLoginAsync(client, email);
        var resp = await SubmitTotpAsync(client, twoFactorCookie!, mfaRm, firstBackupCode);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = _factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();

        session.UsedBackupCodeAtLogin.Should().BeTrue();
    }

    [Fact]
    public async Task Login_with_TOTP_code_sets_UsedBackupCodeAtLogin_false_on_session_row()
    {
        const string email = "totp-1" + EmailSuffix;
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (twoFactorCookie, mfaRm) = await DoCredentialsLoginAsync(client, email);
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await SubmitTotpAsync(client, twoFactorCookie!, mfaRm, code);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();

        session.UsedBackupCodeAtLogin.Should().BeFalse();
    }

    [Fact]
    public async Task Me_returns_UsedBackupCodeAtLastLogin_for_current_session_only()
    {
        const string email = "multi-1" + EmailSuffix;
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        string backupCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await svc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
            backupCode = codes.First();
        }

        // Session A: log in with a backup code. Distinct User-Agent per client — on
        // TestServer both clients share the same IP (RemoteIpAddress is null), and the
        // login dedup (Stage 12.10) revokes the prior live ephemeral session sharing
        // (UserId, UserAgent, IpCreatedAt). Same UA here would make A and B look like
        // the same device, so B's login would revoke A before this test ever reads it.
        // Distinct UAs makes them genuinely different devices, matching the dedup's
        // intended contract, while leaving the per-session flag under test untouched.
        var clientA = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (twoFactorA, mfaRmA) = await DoCredentialsLoginAsync(clientA, email, userAgent: "device-a-test-agent");
        var loginA = await SubmitTotpAsync(clientA, twoFactorA!, mfaRmA, backupCode, userAgent: "device-a-test-agent");
        loginA.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sessionCookieA = ExtractSetCookie(loginA, SessionConstants.SessionCookieName);
        sessionCookieA.Should().NotBeNullOrEmpty();

        // Session B: log in with a real TOTP code on a different HttpClient + device.
        var clientB = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (twoFactorB, mfaRmB) = await DoCredentialsLoginAsync(clientB, email, userAgent: "device-b-test-agent");
        var totpCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var loginB = await SubmitTotpAsync(clientB, twoFactorB!, mfaRmB, totpCode, userAgent: "device-b-test-agent");
        loginB.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sessionCookieB = ExtractSetCookie(loginB, SessionConstants.SessionCookieName);
        sessionCookieB.Should().NotBeNullOrEmpty();

        var meA = await GetMeAsync(clientA, sessionCookieA!);
        var meB = await GetMeAsync(clientB, sessionCookieB!);

        meA.UsedBackupCodeAtLastLogin.Should().BeTrue("session A authenticated via a backup code");
        meB.UsedBackupCodeAtLastLogin.Should().BeFalse("session B authenticated via the authenticator app");
    }

    private async Task<(string?, string?)> DoCredentialsLoginAsync(
        HttpClient client, string email, string? userAgent = null)
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
        if (userAgent is not null) req.Headers.Add("User-Agent", userAgent);
        var resp = await client.SendAsync(req);
        return (ExtractSetCookie(resp, "Identity.TwoFactorUserId"),
                ExtractSetCookie(resp, "Mfa.RememberMe"));
    }

    private async Task<HttpResponseMessage> SubmitTotpAsync(
        HttpClient client, string twoFactorCookie, string? mfaRememberMeCookie, string code,
        string? userAgent = null)
    {
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
        if (userAgent is not null) totpReq.Headers.Add("User-Agent", userAgent);
        return await client.SendAsync(totpReq);
    }

    private static async Task<MeResponseShape> GetMeAsync(HttpClient client, string sessionCookieValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookieValue}");
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<MeResponseShape>())!;
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

    private sealed record MeResponseShape(
        Guid UserId,
        string Email,
        bool TwoFactorEnabled,
        long? LastReauthAt,
        int BackupCodesRemaining,
        bool UsedBackupCodeAtLastLogin);
}
