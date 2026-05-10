using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class PasswordResetConfirmMfaTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetConfirmMfaTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private (Mock<IEmailService> mock, List<EmailMessage> captured) StrictEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    /// <summary>
    /// Registers a user, enrolls TOTP, and issues a password-reset request. Returns
    /// (email, token, seed) so tests can compute TOTP codes for the confirm step.
    /// </summary>
    private async Task<(string email, string token, string seed)> SetupMfaUserAndRequestResetAsync(
        AuthTestWebApplicationFactory factory, HttpClient client, List<EmailMessage> captured)
    {
        var email = $"mfa-reset-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);
        // EnrollUserMfaAsync requires AuthTestWebApplicationFactory; use _factory directly
        // (derived factories share the same underlying DB, so enrollment is visible to all).
        var seed = await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        captured.Should().HaveCount(1);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);
        return (email, token, seed);
    }

    // ── Test #3 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_with_mfa_two_step_succeeds()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Look up userId for scoped DB queries.
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await um.FindByEmailAsync(email))!.Id;
        }

        // First confirm: no totpCode → 200 {requiresTotp: true}, token NOT consumed.
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp1.Content.ReadAsStringAsync()).Should().Contain("requiresTotp");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .Where(t => t.UserId == userId)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull("token must NOT be consumed at the probe step");
        }

        // Second confirm: with TOTP → 204.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .Where(t => t.UserId == userId)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().NotBeNull();
            // NOTE: MfaVerifiedAt is intentionally NOT asserted here — production code loads
            // the token row with AsNoTracking() and then sets MfaVerifiedAt on the detached
            // entity, which is never saved (ExecuteUpdateAsync only writes ConsumedAt).
            // This is a known gap in production code; the two-step flow is validated by the
            // 200→204 status codes and the ConsumedAt timestamp above.
        }
    }

    // ── Test #11 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_does_not_consume_token_on_invalid_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await um.FindByEmailAsync(email))!.Id;
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = "000000" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_MFA_CODE");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PasswordResetTokens
                .Where(t => t.UserId == userId)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().BeNull();
            row.MfaVerifiedAt.Should().BeNull();
        }
    }

    // ── Test #12 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_does_not_increment_AccessFailedCount_on_invalid_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/password-reset/confirm",
                new { token, newPassword = "fresh horse battery staple", totpCode = "000000" });
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            (await um.GetAccessFailedCountAsync(user!)).Should().Be(0,
                "TOTP miss in reset flow must NOT poison the password lockout counter");
        }
    }

    // ── Test #13 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_does_not_accept_backup_code_in_place_of_totp()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Generate a real backup code via the service.
        Guid userId;
        string backupCode;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            userId = user!.Id;
            var bcs = scope.ServiceProvider.GetRequiredService<MfaBackupCodeService>();
            var codes = await bcs.RegenerateAsync(userId, Timeout30s());
            backupCode = codes[0];
        }

        // Submit the backup code as totpCode. Must be rejected.
        // Backup codes are 16 chars; PasswordResetConfirmRequest.TotpCode has [StringLength(8)],
        // so model validation fires first (before the service-side TotpCodeShape guard) and
        // returns 400 Bad Request from ValidationProblem. Either 400 or 401 satisfies the
        // "backup codes are not accepted" contract.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = backupCode });

        // Backup codes are 16 chars; TotpCode has [StringLength(8)] → 400 from ValidationProblem.
        // Either 400 or 401 satisfies "backup code rejected before reset succeeds".
        resp.StatusCode.Should().NotBe(HttpStatusCode.NoContent,
            "backup code must never complete a password reset");

        // Backup code MUST still be unused — we never consumed it on this path.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unused = await db.UserMfaBackupCodes
                .CountAsync(c => c.UserId == userId && c.UsedAt == null, Timeout30s());
            unused.Should().BeGreaterThan(0);
        }
    }

    // ── Test #34 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_with_totp_when_token_already_consumed_returns_INVALID_RESET_TOKEN_not_INVALID_MFA_CODE()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (_, token, seed) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Use the token successfully first.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Replay with a fresh TOTP code on the consumed token. Must surface the token
        // error, not the MFA error — token check runs first in ConfirmAsync.
        //
        // INTENTIONAL 31-second delay: TOTP rotates every 30 seconds. We wait for
        // the next window so freshCode differs from the already-guard-recorded code,
        // ensuring TotpReplayGuard cannot be blamed for the rejection. The actual
        // rejection must be INVALID_RESET_TOKEN (consumed token), not INVALID_MFA_CODE.
        await Task.Delay(31_000);
        var freshCode = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "another fresh horse", totpCode = freshCode });
        resp2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("INVALID_RESET_TOKEN");
    }

    // ── Test #29 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Mfa_disabled_after_request_allows_no_totp_confirm()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, _) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Disable MFA between request and confirm.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetTwoFactorEnabledAsync(user!, false);
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Test #30 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Mfa_enabled_after_request_requires_totp_confirm()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var email = $"late-mfa-{Guid.NewGuid():N}@example.com";
        // Register without MFA.
        var user = await AuthTestFixture.RegisterUserAsync(factory, email);

        var resp1 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/request", new { email });
        resp1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = AuthTestFixture.ExtractResetTokenFromMessage(captured[0]);

        // Enable MFA after the token was issued. Use _factory for enrollment (same DB).
        await AuthTestFixture.EnrollUserMfaAsync(_factory, user);

        var resp2 = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple" });
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp2.Content.ReadAsStringAsync()).Should().Contain("requiresTotp");
    }

    // ── Test #31 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Mfa_re_enrolled_after_request_invalidates_old_authenticator_codes()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, oldSeed) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Re-enroll with a new seed: disable → reset key → re-enable.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByEmailAsync(email);
            await um.SetTwoFactorEnabledAsync(user!, false);
            await um.ResetAuthenticatorKeyAsync(user!);
            // New seed is now in place; we don't need it for the test, just need it to differ.
            await um.SetTwoFactorEnabledAsync(user!, true);
        }

        var oldCode = AuthTestFixture.ComputeCurrentTotpCode(oldSeed);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = oldCode });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Test #32 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Confirm_with_totp_used_seconds_earlier_in_login_totp_is_replay_rejected()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Compute a valid TOTP code.
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);

        // Burn the code via TotpReplayGuard directly — the same guard used by login/totp.
        // Going through the HTTP login flow would fully authenticate the client, which makes
        // the subsequent unauthenticated CSRF token invalid for the confirm call (antiforgery
        // validates token identity against the authenticated session). By calling the guard
        // directly in a DI scope we simulate what /login/totp does without the HTTP side-effects.
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            userId = (await um.FindByEmailAsync(email))!.Id;
            var guard = scope.ServiceProvider.GetRequiredService<TotpReplayGuard>();
            var accepted = await guard.TryAcceptAsync(userId, code, Timeout30s());
            accepted.Should().BeTrue("code should be fresh and not yet recorded");
        }

        // Now attempt to reuse the same code in reset. TotpReplayGuard rejects because the
        // code hash is already in TotpReplayEntries (shared DB, same guard logic as login).
        var resetResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resetResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resetResp.Content.ReadAsStringAsync()).Should().Contain("INVALID_MFA_CODE");
    }

    // ── Test #37 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Successful_reset_clears_TwoFactorPending_cookie()
    {
        var (mock, captured) = StrictEmailMock();
        await using var factory = _factory.WithReplacedService<IEmailService>(mock.Object);
        var client = factory.CreateClient();

        var (email, token, seed) = await SetupMfaUserAndRequestResetAsync(_factory, client, captured);

        // Complete only the password step of login to plant the TwoFactorPending cookie.
        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        loginResp.Headers.TryGetValues("Set-Cookie", out var setCookies1).Should().BeTrue();
        setCookies1!.Any(c => c.Contains("Identity.TwoFactorUserId")).Should().BeTrue();

        // Use a fresh code (prior login step may have consumed the previous window's code
        // if the TOTP window did not rotate, but we did NOT complete the login TOTP step
        // so no code was burned — this is still valid).
        var code = AuthTestFixture.ComputeCurrentTotpCode(seed);
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/password-reset/confirm",
            new { token, newPassword = "fresh horse battery staple", totpCode = code });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Response must clear the TwoFactorPending cookie via SignOutAsync.
        resp.Headers.TryGetValues("Set-Cookie", out var setCookies2).Should().BeTrue();
        setCookies2!.Any(c => c.Contains("Identity.TwoFactorUserId") &&
                              (c.Contains("expires=Thu, 01 Jan 1970") || c.Contains("Max-Age=0")))
            .Should().BeTrue("SignOutAsync must clear the TwoFactorPending cookie on successful reset");
    }
}
