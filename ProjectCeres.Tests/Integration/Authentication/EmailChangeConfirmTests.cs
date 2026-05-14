using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class EmailChangeConfirmTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeConfirmTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    /// <summary>Sets up a fresh user with a pending email-change request and returns the
    /// raw verify token + raw revoke token + the test factory + the user. Caller is
    /// responsible for disposing the returned factory via the await using pattern.</summary>
    private async Task<(WebApplicationFactory<Program> Factory, ApplicationUser User,
                       string OldEmail, string NewEmail,
                       string VerifyToken, string RevokeToken)>
        ArrangePendingChangeAsync(List<EmailMessage> captured)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/request")
        {
            Content = JsonContent.Create(new { newEmail }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var verifyMsg = captured.Single(m => m.To.Address == newEmail);
        var revokeMsg = captured.Single(m => m.To.Address == oldEmail);
        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(verifyMsg);
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(revokeMsg);

        // Clear captured so confirm/revoke assertions don't trip on the request emails.
        captured.Clear();

        return (factory, user, oldEmail, newEmail, verifyToken, revokeToken);
    }

    private static async Task<HttpResponseMessage> PostConfirmAsync(
        WebApplicationFactory<Program> factory, AuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/confirm")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    // ── Test #10 ────────────────────────────────────────────────────────────
    // Happy path: confirm rewrites Email + NormalizedEmail + UserName + NormalizedUserName,
    // sets EmailConfirmed = true.
    [Fact]
    public async Task Confirm_happy_path_updates_email_and_normalized_email_and_username_and_emailconfirmed_true()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um.FindByIdAsync(arr.User.Id.ToString())
            ?? throw new InvalidOperationException("user vanished");

        refreshed.Email.Should().Be(arr.NewEmail);
        refreshed.NormalizedEmail.Should().Be(um.NormalizeEmail(arr.NewEmail));
        refreshed.UserName.Should().Be(arr.NewEmail);
        refreshed.NormalizedUserName.Should().Be(um.NormalizeName(arr.NewEmail));
        refreshed.EmailConfirmed.Should().BeTrue();
    }

    // ── Test #11 ────────────────────────────────────────────────────────────
    // Happy path: all UserSession rows revoked + SecurityStamp regenerated.
    [Fact]
    public async Task Confirm_revokes_all_user_sessions_and_regenerates_security_stamp()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        // Stamp before
        string stampBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByIdAsync(arr.User.Id.ToString());
            stampBefore = u!.SecurityStamp ?? "";
        }

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());
        sessions.Should().NotBeEmpty("MintAuthCookieWithLastReauthAt inserted a UserSession row");
        sessions.Should().OnlyContain(s => s.RevokedAt != null, "all sessions for the user must be revoked");

        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um2.FindByIdAsync(arr.User.Id.ToString());
        refreshed!.SecurityStamp.Should().NotBe(stampBefore, "SecurityStamp must be regenerated");
    }

    // ── Test #12 ────────────────────────────────────────────────────────────
    // Happy path: sibling RevokeOld row consumed atomically with the matched VerifyNew.
    [Fact]
    public async Task Confirm_consumes_sibling_RevokeOld_row()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.ConsumedAt != null,
            "both VerifyNew and RevokeOld must be consumed after a successful /confirm");
    }

    // ── Test #13 ────────────────────────────────────────────────────────────
    // Negative: unknown / malformed token → 401 INVALID_EMAIL_CHANGE_TOKEN.
    [Fact]
    public async Task Confirm_with_invalid_token_returns_401_INVALID_EMAIL_CHANGE_TOKEN()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        var resp = await PostConfirmAsync(factory, _factory, "not-a-real-token");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #14 ────────────────────────────────────────────────────────────
    // Expired token: insert a row directly with ExpiresAt in the past → 401.
    [Fact]
    public async Task Confirm_with_expired_token_returns_401()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-expired-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-expired-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        // Insert a VerifyNew row directly with ExpiresAt = UtcNow.AddMinutes(-1).
        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<EmailChangeTokenGenerator>();
            var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
            rawToken = generator.Generate();
            db.EmailChangeTokens.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = EmailChangeTokenPurpose.VerifyNew,
                NewEmail = newEmail,
                TokenLookup = lookupHasher.ComputeLookup(rawToken),
                TokenHash = generator.Hash(rawToken),
                CreatedAt = DateTime.UtcNow.AddMinutes(-31),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
                ConsumedAt = null,
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await PostConfirmAsync(factory, _factory, rawToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #15 ────────────────────────────────────────────────────────────
    // Already-consumed token → 401.
    [Fact]
    public async Task Confirm_with_already_consumed_token_returns_401()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var first = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #16 ────────────────────────────────────────────────────────────
    // Negative: another user grabs the new email between request and confirm.
    // /confirm returns 422 EMAIL_ALREADY_IN_USE; the matched VerifyNew row must
    // NOT be consumed (caller can /revoke to clean up the pair).
    [Fact]
    public async Task Confirm_with_collision_grabbed_between_request_and_confirm_returns_422_and_does_NOT_consume_token()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        // Race: another user registers with arr.NewEmail before confirm.
        await AuthTestFixture.RegisterUserAsync(_factory, arr.NewEmail);

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("EMAIL_ALREADY_IN_USE");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifyRow = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .SingleAsync(t => t.UserId == arr.User.Id && t.Purpose == EmailChangeTokenPurpose.VerifyNew, Timeout30s());
        verifyRow.ConsumedAt.Should().BeNull(
            "collision-at-confirm must NOT consume the token — caller can /revoke to clean up");
    }

    // ── Test #17 ────────────────────────────────────────────────────────────
    // Negative-assertion: /confirm does NOT clear lockout. Pre-set LockoutEnd
    // and AccessFailedCount; verify both are unchanged after a successful confirm.
    // Email change is NOT a recovery flow; it must not unlock a guess-locked account.
    [Fact]
    public async Task Confirm_does_NOT_clear_lockout()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        // Pre-set lockout state.
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        const int failedCount = 7;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByIdAsync(arr.User.Id.ToString());
            await um.SetLockoutEndDateAsync(u!, lockoutEnd);
            // SetLockoutEndDateAsync persists; bump AccessFailedCount via raw column.
            for (var i = 0; i < failedCount; i++) await um.AccessFailedAsync(u!);
        }

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um2.FindByIdAsync(arr.User.Id.ToString());
        refreshed!.LockoutEnd.Should().NotBeNull("LockoutEnd must be unchanged");
        refreshed.LockoutEnd.Should().BeCloseTo(lockoutEnd, TimeSpan.FromSeconds(5));
        refreshed.AccessFailedCount.Should().BeGreaterThanOrEqualTo(failedCount,
            "AccessFailedCount must be unchanged (any value ≥ pre-confirm value is acceptable; AccessFailedAsync may have incremented past lockout-trigger before SetLockoutEndDateAsync)");
    }

    // ── Test #18 ────────────────────────────────────────────────────────────
    // Confirmation emails sent to BOTH new and old addresses. The new-address
    // notification confirms the new state; the old-address notification closes
    // the loop for the legitimate user who may still control the old inbox
    // (per security-model.md "Notify the old address on successful completion
    // of the change").
    [Fact]
    public async Task Confirm_sends_change_confirmed_email_to_BOTH_new_and_old_addresses()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostConfirmAsync(factory, _factory, arr.VerifyToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        captured.Should().Contain(m => m.To.Address == arr.NewEmail && m.Subject.Contains("change confirmed"),
            "a change-confirmed notification must be sent to the new (now address-of-record) address");
        captured.Should().Contain(m => m.To.Address == arr.OldEmail && m.Subject.Contains("change confirmed"),
            "a change-confirmed notification must also be sent to the old address per security-model.md");
    }
}
