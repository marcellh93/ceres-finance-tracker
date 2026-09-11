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
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication.Mfa;

[Collection("IntegrationParallel4")]
public class MfaRegenerateTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;
    public MfaRegenerateTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@regen-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    /// <summary>
    /// Registers a user, enrolls MFA, generates initial backup codes, then performs
    /// the full two-step login (credentials + TOTP). Returns the client (cookie-jar
    /// disabled), the session cookie, the seed, and the user object so callers can
    /// make user-bound CSRF requests against authenticated endpoints.
    /// </summary>
    private async Task<(HttpClient client, string sessionCookie, string seed, ApplicationUser user)>
        SetupAuthenticatedMfaUserAsync(string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Generate the initial set of 10 backup codes. EnrollUserMfaAsync enables
        // TwoFactor but does not call GenerateAndPersistAsync — the enroll/verify
        // endpoint does that in production. We do it here so counts can be verified.
        using (var scope = _factory.Services.CreateScope())
        {
            var bc = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            await bc.GenerateAndPersistAsync(user.Id, CancellationToken.None);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1 — credentials login → get Identity.TwoFactorUserId cookie
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

        // Step 2 — TOTP login → get session cookie (anonymous CSRF — user is not yet signed-in)
        var loginCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
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

        return (client, sessionCookie, seed, user);
    }

    /// <summary>
    /// POSTs to the regenerate endpoint using a user-bound CSRF token and the
    /// established session cookie. The endpoint takes no body after Task 11;
    /// the gate is [RequireRecentAuth] which checks LastReauthAt baked into the
    /// session cookie at login time.
    /// </summary>
    private async Task<HttpResponseMessage> PostRegenAsync(
        HttpClient client, string sessionCookie, Guid userId)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/backup-codes/regenerate");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task Regenerate_WithoutBody_Succeeds_AfterFreshLogin()
    {
        // After Task 11 the endpoint takes no body; [RequireRecentAuth] is the gate.
        // Login stamps LastReauthAt so the gate passes immediately.
        var (client, sessionCookie, _, user) = await SetupAuthenticatedMfaUserAsync("no-code@regen-test.local");

        var resp = await PostRegenAsync(client, sessionCookie, user.Id);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Regenerate_ReplacesCodes()
    {
        var (client, sessionCookie, _, user) = await SetupAuthenticatedMfaUserAsync("valid@regen-test.local");

        // No body, no TOTP — the gate is RequireRecentAuth (LastReauthAt claim from login).
        var resp = await PostRegenAsync(client, sessionCookie, user.Id);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var newCodes = body.GetProperty("backupCodes");
        newCodes.GetArrayLength().Should().Be(10);

        // Old codes wiped, exactly 10 new active codes in DB.
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unused = await verifyDb.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == user.Id && c.UsedAt == null).CountAsync();
        unused.Should().Be(10);
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
