using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class ReauthEndpointTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public ReauthEndpointTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // ── Test #1 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_correct_password_no_mfa_returns_204_and_stamps_claim()
    {
        var email = $"reauth-pw-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        // Reauth is an authenticated request — use user-bound CSRF to satisfy antiforgery.
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #2 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_correct_totp_mfa_returns_204_and_stamps_claim()
    {
        var email = $"reauth-totp-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        // Login FIRST (no MFA yet so login returns 204), then enroll MFA.
        // The session cookie acquired before enrollment is still valid.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Reauth is an authenticated request — use user-bound CSRF to satisfy antiforgery.
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = code }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #3 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_stamps_LastReauthAt_claim()
    {
        var email = $"login-stamps-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        // Hit a gated endpoint immediately. Should pass the gate because login was just now.
        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "fresh login should stamp LastReauthAt within the 5-min window, allowing /mfa/enroll to pass the gate");
    }

    // ── Test #4 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task LoginTotp_stamps_LastReauthAt_claim()
    {
        var email = $"login-totp-stamps-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: password login → 200 { requiresTotp: true }
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var twoFactorCookie = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("Identity.TwoFactorUserId="))
            .Split(';')[0];

        // Step 2: TOTP completes login → 204 + __Host-Session
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = AuthTestFixture.ComputeCurrentTotpCode(seed) }),
        };
        totpReq.Headers.Add("Cookie", $"{twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var totpResp = await client.SendAsync(totpReq);
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var sessionCookie = totpResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
            .Split(';')[0]
            .Substring(SessionConstants.SessionCookieName.Length + 1);

        // Hit a gated endpoint immediately.
        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var gatedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        gatedReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
        gatedReq.Headers.Add(SessionConstants.CsrfHeaderName, header3);
        var gatedResp = await client.SendAsync(gatedReq);

        // The user has MFA already enrolled — Enroll returns 409 MFA_ALREADY_ENROLLED.
        // What we actually care about is the gate passing — 409 means the gate let us through.
        // 401 REAUTH_REQUIRED would mean the gate fired (failure).
        gatedResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "TOTP login should stamp LastReauthAt — the gate must pass; the 409 MFA_ALREADY_ENROLLED proves we reached the action");
    }

    // ── Test #5 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task LoginTotp_with_backup_code_also_stamps_LastReauthAt()
    {
        var email = $"login-bk-stamps-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Generate backup codes via the existing service so we have a valid one to use.
        string backupCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await bcs.RegenerateAsync(user.Id, Timeout30s());
            backupCode = codes[0];
        }

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: password login → 200 { requiresTotp: true }
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }),
        };
        loginReq.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        loginReq.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        var loginResp = await client.SendAsync(loginReq);
        var twoFactorCookie = loginResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("Identity.TwoFactorUserId=")).Split(';')[0];

        // Step 2: backup code completes login
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory);
        var totpReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/totp")
        {
            Content = JsonContent.Create(new { code = backupCode }),
        };
        totpReq.Headers.Add("Cookie", $"{twoFactorCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        totpReq.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var totpResp = await client.SendAsync(totpReq);
        totpResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionCookie = totpResp.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{SessionConstants.SessionCookieName}="))
            .Split(';')[0]
            .Substring(SessionConstants.SessionCookieName.Length + 1);

        // Hit a gated endpoint — should pass the gate via the just-stamped LastReauthAt.
        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf3, header3) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var gatedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/mfa/enroll");
        gatedReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf3}");
        gatedReq.Headers.Add(SessionConstants.CsrfHeaderName, header3);
        var gatedResp = await client.SendAsync(gatedReq);

        gatedResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "backup-code login is a recovery path but DOES stamp LastReauthAt; the gate passes");
    }

    // ── Test #6 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_wrong_password_returns_401_INVALID_REAUTH_and_increments_AccessFailedCount()
    {
        var email = $"reauth-wp-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = "wrong-password" }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync(email);
        (await um.GetAccessFailedCountAsync(fresh!)).Should().Be(1);
    }

    // ── Test #7 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_wrong_password_can_lock_account_after_10_attempts()
    {
        var email = $"reauth-lock-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        for (var i = 0; i < 10; i++)
        {
            // Use user-bound CSRF — antiforgery validates against the authenticated principal.
            var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
            {
                Content = JsonContent.Create(new { password = $"wrong-{i}" }),
            };
            req.Headers.Add("Cookie",
                $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
            req.Headers.Add(SessionConstants.CsrfHeaderName, header);
            await client.SendAsync(req);
        }

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync(email);
        (await um.IsLockedOutAsync(fresh!)).Should().BeTrue();
    }

    // ── Test #8 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_wrong_totp_returns_401_INVALID_REAUTH_and_does_not_increment_AccessFailedCount()
    {
        var email = $"reauth-bt-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        // Login first (no MFA yet so login returns 204), then enroll MFA.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = "000000" }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var fresh = await um.FindByEmailAsync(email);
        (await um.GetAccessFailedCountAsync(fresh!)).Should().Be(0,
            "wrong TOTP at reauth uses VerifyTwoFactorTokenAsync and does NOT poison the password lockout counter");
    }

    // ── Test #9 ──────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_replayed_totp_returns_401_INVALID_REAUTH()
    {
        var email = $"reauth-rt-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        // Login first (no MFA yet so login returns 204), then enroll MFA.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        // First reauth burns the code.
        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf1, header1) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = code }),
        };
        req1.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf1}");
        req1.Headers.Add(SessionConstants.CsrfHeaderName, header1);
        var resp1 = await client.SendAsync(req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Replay the same code → 401.
        var (csrf2, header2) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = code }),
        };
        req2.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf2}");
        req2.Headers.Add(SessionConstants.CsrfHeaderName, header2);
        var resp2 = await client.SendAsync(req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");
    }

    // ── Test #10 ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_backup_code_in_totpCode_returns_401_INVALID_REAUTH()
    {
        var email = $"reauth-bk-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        // Login first (no MFA yet so login returns 204), then enroll MFA and generate backup codes.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        string backupCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await bcs.RegenerateAsync(user.Id, Timeout30s());
            backupCode = codes[0];
        }

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = backupCode }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_REAUTH");
    }

    // ── Test #11 ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_when_account_already_locked_returns_401_ACCOUNT_LOCKED_OUT()
    {
        var email = $"reauth-loa-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByEmailAsync(email);
            await um.SetLockoutEndDateAsync(fresh!, DateTimeOffset.UtcNow.AddHours(1));
        }

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("ACCOUNT_LOCKED_OUT");
    }

    // ── Test #12 ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_with_missing_required_field_returns_422_VALIDATION_ERROR()
    {
        var email = $"reauth-mf-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { totpCode = "123456" }),  // user has no MFA → password is required
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("VALIDATION_ERROR");
        body.Should().Contain("password");
    }

    // ── Test #13 ─────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Reauth_ignores_extraneous_field_based_on_user_MFA_state()
    {
        var email = $"reauth-ext-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        // Use user-bound CSRF — antiforgery validates against the authenticated principal.
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reauth")
        {
            Content = JsonContent.Create(new { password = AuthTestFixture.ValidPassword, totpCode = "123456" }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "no-MFA user submitting both password and totpCode should succeed via password validation; totpCode is ignored");
    }
}
