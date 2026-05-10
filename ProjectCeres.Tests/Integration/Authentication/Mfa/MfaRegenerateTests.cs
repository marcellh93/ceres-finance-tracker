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

[Collection("IntegrationTests")]
public class MfaRegenerateTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public MfaRegenerateTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@regen-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
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
    /// POSTs to an authenticated MFA endpoint using a user-bound CSRF token and the
    /// established session cookie. Mirrors the pattern used by MfaEnrollmentTests and
    /// MfaCacheControlTests for post-login requests.
    /// </summary>
    private async Task<HttpResponseMessage> PostRegenAsync(
        HttpClient client, string sessionCookie, Guid userId, object body)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/backup-codes/regenerate")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task Regenerate_WithoutTotpCode_Returns422()
    {
        var (client, sessionCookie, _, user) = await SetupAuthenticatedMfaUserAsync("no-code@regen-test.local");

        var resp = await PostRegenAsync(client, sessionCookie, user.Id, new { });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Regenerate_WithInvalidTotp_Returns401_DoesNotWipeCodes()
    {
        var (client, sessionCookie, _, user) = await SetupAuthenticatedMfaUserAsync("bad-code@regen-test.local");

        // Capture the original code count.
        int originalCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalCount = await db.UserMfaBackupCodes.Where(c => c.UserId == user.Id).CountAsync();
        }
        originalCount.Should().Be(10, "setup generated 10 backup codes");

        var resp = await PostRegenAsync(client, sessionCookie, user.Id, new { totpCode = "000000" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_MFA_CODE");

        // Codes are still intact.
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var afterCount = await verifyDb.UserMfaBackupCodes.Where(c => c.UserId == user.Id).CountAsync();
        afterCount.Should().Be(10);
    }

    [Fact]
    public async Task Regenerate_WithValidTotp_ReplacesCodes()
    {
        var (client, sessionCookie, seed, user) = await SetupAuthenticatedMfaUserAsync("valid@regen-test.local");

        // Wait so the regen TOTP code is a different time-step than the login code (replay guard).
        // TOTP rotates every 30s; sleep 31s to guarantee a fresh window.
        await Task.Delay(TimeSpan.FromSeconds(31));

        var regenCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await PostRegenAsync(client, sessionCookie, user.Id, new { totpCode = regenCode });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var newCodes = body.GetProperty("backupCodes");
        newCodes.GetArrayLength().Should().Be(10);

        // Old codes wiped, exactly 10 new active codes in DB.
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unused = await verifyDb.UserMfaBackupCodes.Where(c => c.UserId == user.Id && c.UsedAt == null).CountAsync();
        unused.Should().Be(10);
    }

    [Fact]
    public async Task Regenerate_RateLimitedPerUser()
    {
        // The spec called for an auth-mfa-by-user rate-limit policy on the regen endpoint.
        // Gap 4 implementation did not add it. This test confirms the gap: 11 calls in
        // quick succession must eventually return 429. If the test FAILS (i.e. none of
        // the 11 calls returns 429), that confirms the missing policy — stop and report.

        var (client, sessionCookie, seed, user) = await SetupAuthenticatedMfaUserAsync("rl-regen@regen-test.local");

        // Wait so regen codes are in a different TOTP window than the login code.
        await Task.Delay(TimeSpan.FromSeconds(31));

        HttpResponseMessage? last429 = null;
        for (int i = 0; i < 11; i++)
        {
            var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
            var resp = await PostRegenAsync(client, sessionCookie, user.Id, new { totpCode = code });
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                last429 = resp;
                break;
            }
            // After the first successful regen the old codes are replaced; the seed stays
            // the same (authenticator key is unchanged), so we can keep computing codes.
            // Each call within the same TOTP window will replay the same code and get 401
            // (either replay-guard or TOTP reject) — wait for the next window to get a
            // fresh code. This is acceptable: we want to see if the rate limiter fires at
            // all, not just on perfectly valid codes.
        }

        last429.Should().NotBeNull(
            "regenerate must be rate-limited (auth-mfa-by-user policy); 11 rapid calls must eventually return 429. " +
            "If this fails, the Gap-4 implementation skipped the rate-limit — see spec § Gap 4");
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
