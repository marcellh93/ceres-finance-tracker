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

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 13.9: a sealed account (ApplicationUser.SealedAt set) is rejected on every
/// authenticated request and refused at login, mirroring the revoked-session behavior.
/// </summary>
[Collection("IntegrationParallel4")]
public class AccountSealTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;

    public AccountSealTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, "@seal-test.local");

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in userManager.Users.Where(u => u.Email!.EndsWith("@seal-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            await userManager.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task Request_from_sealed_account_returns_401()
    {
        await AuthTestFixture.RegisterUserAsync(_factory, "sealed-req@seal-test.local");
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "sealed-req@seal-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = ExtractSetCookie(loginResp, SessionConstants.SessionCookieName);
        sessionCookie.Should().NotBeNullOrEmpty();

        // Seal the account directly in the DB, mid-session — mimics an erasure request
        // sealing the account after the user already has a live cookie.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email == "sealed-req@seal-test.local")
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        }

        // Subsequent request with the still-valid session cookie is rejected because
        // the account is now sealed — same 401 as a revoked session.
        var probe = new HttpRequestMessage(HttpMethod.Get, "/api/transactions");
        probe.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        var probed = await client.SendAsync(probe);
        probed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_for_sealed_account_is_refused()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "sealed-login@seal-test.local");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        }

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client, "/api/auth/login",
            new { email = user.Email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.error.code.Should().Be("ACCOUNT_SEALED");
    }

    [Fact]
    public async Task LoginTotp_with_valid_code_for_sealed_account_is_refused_no_session_issued()
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, "sealed-mfa@seal-test.local");
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: credentials login while NOT yet sealed — obtains the half-authenticated
        // Identity.TwoFactorUserId cookie (RequiresTwoFactor branch runs before any seal
        // check would matter here, since the account isn't sealed yet at this point).
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "sealed-mfa@seal-test.local",
                password = AuthTestFixture.ValidPassword,
                rememberMe = false
            }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var twoFactorCookie = ExtractSetCookie(loginResp, "Identity.TwoFactorUserId");
        twoFactorCookie.Should().NotBeNullOrEmpty();

        // Step 2: seal the account mid-MFA-flow, after the password step, before TOTP.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        }

        // Step 3: submit a VALID TOTP code. This is the load-bearing assertion — without
        // the LoginTotp seal check, this would return 204 + a session cookie despite the
        // account being sealed.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var (totpCsrf, totpHeader) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code }),
        };
        totpReq.Headers.Add("Cookie",
            $"Identity.TwoFactorUserId={twoFactorCookie}; {SessionConstants.CsrfCookieName}={totpCsrf}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, totpHeader);
        var totpResp = await client.SendAsync(totpReq);

        totpResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await totpResp.Content.ReadFromJsonAsync<ErrorEnvelope>();
        body!.error.code.Should().Be("ACCOUNT_SEALED");
        var setCookies = totpResp.Headers.TryGetValues("Set-Cookie", out var v) ? v.ToList() : new List<string>();
        setCookies.Should().NotContain(c => c.StartsWith($"{SessionConstants.SessionCookieName}="));

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessionCount = await verifyDb.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id).CountAsync();
        sessionCount.Should().Be(0, "a sealed account must never get a UserSession row issued via LoginTotp");
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

    private sealed class ErrorEnvelope
    {
        public ErrorBody error { get; set; } = null!;
    }

    private sealed class ErrorBody
    {
        public string code { get; set; } = "";
        public string message { get; set; } = "";
    }
}
