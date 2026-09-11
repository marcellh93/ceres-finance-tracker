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
/// Rate-limit tests for MfaController endpoints. Uses RateLimitedAuthTestWebApplicationFactory
/// so the real in-process auth-mfa-by-user sliding-window policy is active.
/// Must live in the "RateLimitTests" collection so partition state does not leak
/// into the IntegrationTests collection's no-op factory.
/// </summary>
[Collection("MfaRateLimitTests")]
public class MfaRegenerateRateLimitTests : IAsyncLifetime
{
    private readonly MfaRateLimitedAuthTestWebApplicationFactory _factory;
    public MfaRegenerateRateLimitTests(MfaRateLimitedAuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@mfa-rl-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await db.UserMfaBackupCodes.IgnoreQueryFilters().Where(c => c.UserId == u.Id).ExecuteDeleteAsync();
            await db.TotpReplayEntries.IgnoreQueryFilters().Where(e => e.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
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
    public async Task Regenerate_RateLimitedPerUser()
    {
        // The spec called for an auth-mfa-by-user rate-limit policy on the regen endpoint.
        // Gap 4 implementation did not add it. This test confirms the fix: 11 calls in
        // quick succession must eventually return 429.

        var (client, sessionCookie, _, user) = await SetupAuthenticatedMfaUserAsync("rl-regen@mfa-rl-test.local");

        // No body or TOTP required after Task 11; [RequireRecentAuth] gates via LastReauthAt
        // stamped at login. Send 11 rapid no-body POSTs; the rate limiter must fire before
        // all 11 complete.
        HttpResponseMessage? last429 = null;
        for (int i = 0; i < 11; i++)
        {
            var resp = await PostRegenAsync(client, sessionCookie, user.Id);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                last429 = resp;
                break;
            }
        }

        last429.Should().NotBeNull(
            "regenerate must be rate-limited (auth-mfa-by-user policy); 11 rapid calls must eventually return 429. " +
            "If this fails, the Gap B implementation missed the rate-limit — see spec § Gap B");
    }

    // Stage 6c.2 follow-up: the AuthMfaByUser partitioner's lambda reads
    // httpContext.User?.FindFirst(NameIdentifier) BEFORE UseAuthentication has run,
    // so it always falls back to the shared "anonymous-mfa" bucket. The existing
    // Regenerate_RateLimitedPerUser test only proves "any 429 fires" so it can't
    // catch the partition miss. This test proves the partition is keyed per user:
    // user A's exhaustion must NOT also exhaust user B.
    [Fact]
    public async Task MfaRegenerate_rate_limit_is_partitioned_by_user()
    {
        var (clientA, sessionA, _, userA) = await SetupAuthenticatedMfaUserAsync("rl-partA@mfa-rl-test.local");

        // Drain user A's full budget — must produce at least one 429 to prove A is exhausted.
        var sawAnyA429 = false;
        for (int i = 0; i < 15; i++)
        {
            var resp = await PostRegenAsync(clientA, sessionA, userA.Id);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawAnyA429 = true;
            }
        }
        sawAnyA429.Should().BeTrue("user A's budget must be exhausted before testing partition isolation");

        // Now user B — in the SAME 60s sliding window — must NOT see 429. If the
        // partitioner is collapsed onto a single shared bucket (the bug), B inherits
        // A's exhausted state and returns 429 immediately.
        var (clientB, sessionB, _, userB) = await SetupAuthenticatedMfaUserAsync("rl-partB@mfa-rl-test.local");

        var bResp = await PostRegenAsync(clientB, sessionB, userB.Id);

        bResp.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "user B's first regenerate call must not be rate-limited just because user A exhausted A's bucket; " +
            "if this fires, the AuthMfaByUser partitioner is collapsing all users into the 'anonymous-mfa' fallback bucket. " +
            "Fix: mirror AuthReauthByUser by calling httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme).Wait() " +
            "before reading the NameIdentifier claim.");
    }

    // Stage 6c.2 follow-up — pin the partitioner's "anonymous-mfa" fallback as
    // unreachable on the production path. [Authorize] gates the endpoint; an
    // anonymous request must short-circuit at authentication with 401 BEFORE the
    // rate limiter ever decrements a permit. If this ever returns 429, the gate
    // ordering has changed and anonymous attackers can drain the shared bucket
    // without ever authenticating.
    [Fact]
    public async Task MfaRegenerate_anonymous_request_returns_401_not_429()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/backup-codes/regenerate");
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous requests to MFA endpoints must be rejected by [Authorize] before the rate limiter sees them. " +
            "Returning 429 would mean an unauthenticated attacker can exhaust the anonymous-mfa partition shared with " +
            "any legitimate request that fails authentication.");
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
