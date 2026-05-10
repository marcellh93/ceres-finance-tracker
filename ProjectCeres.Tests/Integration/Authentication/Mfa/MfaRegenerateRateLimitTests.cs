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
/// Rate-limit tests for MfaController endpoints. Uses RateLimitedAuthTestWebApplicationFactory
/// so the real in-process auth-mfa-by-user sliding-window policy is active.
/// Must live in the "RateLimitTests" collection so partition state does not leak
/// into the IntegrationTests collection's no-op factory.
/// </summary>
[Collection("MfaRateLimitTests")]
public class MfaRegenerateRateLimitTests : IAsyncLifetime
{
    private readonly RateLimitedAuthTestWebApplicationFactory _factory;
    public MfaRegenerateRateLimitTests(RateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@mfa-rl-test.local")).ToList())
        {
            await db.UserSessions.Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            await um.DeleteAsync(u);
        }
    }

    private async Task<(HttpClient client, string sessionCookie, string seed, ApplicationUser user)>
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

        // Step 2 — TOTP login → get session cookie
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
    public async Task Regenerate_RateLimitedPerUser()
    {
        // The spec called for an auth-mfa-by-user rate-limit policy on the regen endpoint.
        // Gap 4 implementation did not add it. This test confirms the fix: 11 calls in
        // quick succession must eventually return 429.

        var (client, sessionCookie, seed, user) = await SetupAuthenticatedMfaUserAsync("rl-regen@mfa-rl-test.local");

        // Wait so regen codes are in a different TOTP window than the login code.
        await Task.Delay(TimeSpan.FromSeconds(31));

        HttpResponseMessage? last429 = null;
        for (int i = 0; i < 11; i++)
        {
            var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
            var resp = await PostRegenAsync(client, sessionCookie, user.Id, new { totpCode = code });
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
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
            "If this fails, the Gap B implementation missed the rate-limit — see spec § Gap B");
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
