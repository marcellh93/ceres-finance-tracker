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
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

/// <summary>
/// Stage 9.6 — Pins the contract of POST /api/auth/mfa/disable.
/// Mirrors the MfaRegenerateTests harness exactly (same xUnit collection,
/// same login → TOTP step → session-cookie flow, same per-test cleanup).
/// </summary>
[Collection("IntegrationTests")]
public class MfaDisableTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public MfaDisableTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@disable-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    private async Task<(HttpClient client, string sessionCookie, ApplicationUser user)>
        SetupAuthenticatedMfaUserAsync(string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf1}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header1);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        var loginCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = loginCode }),
        };
        totpReq.Headers.Add("Cookie", $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var totpResp = await client.SendAsync(totpReq);
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sessionCookie = ExtractSetCookie(totpResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty();

        return (client, sessionCookie!, user);
    }

    private async Task<HttpResponseMessage> PostDisableAsync(
        HttpClient client, string sessionCookie, Guid userId)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/disable");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task Disable_ReturnsNoContent_AfterFreshLogin()
    {
        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync("happy@disable-test.local");
        var resp = await PostDisableAsync(client, sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Disable_FlipsTwoFactorEnabled_False()
    {
        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync("flag@disable-test.local");

        var resp = await PostDisableAsync(client, sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um.FindByIdAsync(user.Id.ToString());
        refreshed!.TwoFactorEnabled.Should().BeFalse("disable must clear the flag");
    }

    [Fact]
    public async Task Disable_PurgesBackupCodes()
    {
        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync("purge@disable-test.local");

        using (var beforeScope = _factory.Services.CreateScope())
        {
            var beforeDb = beforeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var beforeCount = await beforeDb.UserMfaBackupCodes.IgnoreQueryFilters()
                .Where(c => c.UserId == user.Id).CountAsync();
            beforeCount.Should().Be(10, "enrolment must have generated 10 codes before disable");
        }

        var resp = await PostDisableAsync(client, sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var afterScope = _factory.Services.CreateScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var afterCount = await afterDb.UserMfaBackupCodes.IgnoreQueryFilters()
            .Where(c => c.UserId == user.Id).CountAsync();
        afterCount.Should().Be(0, "disable must purge all persisted backup-code rows so old codes can't be used after re-enrolment");
    }

    [Fact]
    public async Task Disable_ResetsAuthenticatorKey()
    {
        // Disable must reset the authenticator key so a re-enrol starts with a fresh secret.
        var (client, sessionCookie, user) = await SetupAuthenticatedMfaUserAsync("key@disable-test.local");

        string? keyBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByIdAsync(user.Id.ToString());
            keyBefore = await um.GetAuthenticatorKeyAsync(u!);
            keyBefore.Should().NotBeNullOrEmpty("user enrolled MFA, key should exist");
        }

        var resp = await PostDisableAsync(client, sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var afterScope = _factory.Services.CreateScope();
        var afterUm = afterScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var afterUser = await afterUm.FindByIdAsync(user.Id.ToString());
        var keyAfter = await afterUm.GetAuthenticatorKeyAsync(afterUser!);
        keyAfter.Should().NotBe(keyBefore, "disable must reset the authenticator key");
    }

    [Fact]
    public async Task Disable_WhenMfaNotEnabled_Returns409()
    {
        // Register but DO NOT enroll MFA. Use a fresh client without MFA setup.
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "not-enabled@disable-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Single-step login (no MFA).
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent, "non-MFA login should issue session immediately");
        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName)!;

        var resp = await PostDisableAsync(client, sessionCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("MFA_NOT_ENABLED");
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
