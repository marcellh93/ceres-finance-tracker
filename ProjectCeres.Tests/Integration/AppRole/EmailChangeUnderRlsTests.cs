using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d Task 9 — the DISCOVER test of the suite. Email-change confirm is the only auth
/// write-flow whose service does NOT self-scope: EmailChangeService.ConfirmAsync is
/// [RlsBypassJustified] and uses IgnoreQueryFilters() for its reads, but IgnoreQueryFilters
/// strips only EF's query filter — it does NOT bypass Postgres RLS. The consume-write, sibling
/// consume, and session-revoke ExecuteUpdateAsync calls all hit the DB under ceres_app, and the
/// /confirm endpoint is AllowAnonymous, so there is no authenticated principal to set
/// app.current_user_ref. Unlike Task 7 (PasswordResetService) and Task 8 (EmailConfirmationService),
/// ConfirmAsync opens NO BeginPreAuthUserScopeAsync(match.UserId). This test genuinely discovers
/// whether those writes succeed under ceres_app: a 204 means the GUC is already covered (test-only);
/// a 42501 on a confirm-flow write is a latent production gap to escalate, NOT to fix here.
/// </summary>
[Collection("AppRoleTests")]
public class EmailChangeUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public EmailChangeUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task EmailChange_confirm_under_ceres_app_consumes_tokens_and_updates_email()
    {
        // Capture the raw VerifyNew token by mocking IEmailService — the token is delivered only
        // by email (it is not returned in any HTTP response). Fork the shared ceres_app-wired
        // factory via WithWebHostBuilder so the fork preserves UseAppRoleConnection => true; the
        // HTTP flow runs on the fork (under ceres_app) while seed/assert run on the original
        // Factory (same project_ceres_test DB, shared state).
        var captured = new List<EmailMessage>();
        var strictMock = new Mock<IEmailService>(MockBehavior.Strict);
        strictMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });

        await using var httpFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton(strictMock.Object);
            }));
        var client = httpFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: seed a confirmed user. Register via the production HTTP endpoint (it owns its
        // PreAuthUserScope, so the category seed passes RLS under ceres_app), then confirm the
        // email via the BYPASSRLS admin context — login requires a confirmed account.
        var oldEmail = $"emailchange-old-{Marker}@approle-test.local";
        var newEmail = $"emailchange-new-{Marker}@approle-test.local";
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/register", new { email = oldEmail, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == oldEmail);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        // Step 2: fresh login stamps LastReauthAt on the session cookie, satisfying [RequireRecentAuth]
        // on /email-change/request. LoginViaHttpAsync is the recent-auth mechanism here — its session
        // write owns its scope post-PasswordSignInAsync (the Task 4 fix), unlike MintAuthCookieWith-
        // LastReauthAt which would 42501 inserting a UserSession with no established user scope.
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(Factory, client, oldEmail);

        // Step 3: request the email change (authenticated). Issues the VerifyNew + RevokeOld pair and
        // sends both emails (captured). 202 Accepted per EmailChangeController.RequestChange.
        var requestResp = await SendAuthedAsync(
            client, "/api/auth/email-change/request", sessionCookie, new { newEmail });
        requestResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Capture the VerifyNew raw token. RequestAsync builds the verify URL as
        // {base}/email-change/confirm#token={raw} and the revoke URL as
        // {base}/email-change/revoke#token={raw}; select the verify email by its confirm URL.
        var verifyEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("email-change/confirm#token="),
            "the request endpoint issues exactly one VerifyNew email (confirm URL)").Which;
        var rawVerifyToken = AuthTestFixture.ExtractResetTokenFromMessage(verifyEmail);

        // Step 4: confirm under ceres_app (anonymous, token). A 500 here means a confirm-flow WRITE
        // (token consume, sibling consume, or session revoke) hit RLS 42501 — i.e. ConfirmAsync's
        // IgnoreQueryFilters reads do NOT bypass RLS and the service opens no BeginPreAuthUserScopeAsync,
        // so the latent gap surfaces. A non-204 is the escalation signal (do NOT fix here).
        var confirmResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/email-change/confirm", new { token = rawVerifyToken });
        confirmResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "email-change confirm must persist under ceres_app");

        // Step 5: the token pair is consumed AND the user's email is updated, seen via the owner's
        // ceres_app context.
        await using (var app = Factory.NewAppContext(_userId))
        {
            var consumed = await app.Context.EmailChangeTokens
                .CountAsync(t => t.UserId == _userId && t.ConsumedAt != null);
            consumed.Should().BeGreaterThanOrEqualTo(1, "the confirm flow must stamp ConsumedAt on the token row(s)");

            var updated = await app.Context.Users
                .CountAsync(u => u.Id == _userId && u.Email == newEmail);
            updated.Should().Be(1, "confirm must update the owner's Email to the new address");
        }

        // Step 6: positive + negative RLS control — both token rows (VerifyNew + RevokeOld) are
        // visible to the owner under ceres_app and to no one else. One /request issues exactly two
        // rows; confirm consumes (updates) them rather than adding more.
        await AssertRlsVisibility<EmailChangeToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 2);
    }

    [Fact]
    public async Task EmailChange_revoke_under_ceres_app_consumes_tokens_and_leaves_email_unchanged()
    {
        // Same fork pattern as the confirm test: capture both raw tokens via a strict IEmailService
        // mock; the HTTP flow runs on a ceres_app-wired fork while seed/assert run on Factory.
        var captured = new List<EmailMessage>();
        var strictMock = new Mock<IEmailService>(MockBehavior.Strict);
        strictMock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                captured.Add(m);
                return Task.CompletedTask;
            });

        await using var httpFactory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton(strictMock.Object);
            }));
        var client = httpFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Step 1: seed a confirmed user (register via HTTP under ceres_app, confirm via admin context).
        var oldEmail = $"emailchange-rev-old-{Marker}@approle-test.local";
        var newEmail = $"emailchange-rev-new-{Marker}@approle-test.local";
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/register", new { email = oldEmail, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == oldEmail);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        // Step 2: fresh login satisfies [RequireRecentAuth] on /email-change/request.
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(Factory, client, oldEmail);

        // Step 3: request the change (issues VerifyNew + RevokeOld; both emails captured).
        var requestResp = await SendAuthedAsync(
            client, "/api/auth/email-change/request", sessionCookie, new { newEmail });
        requestResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Capture the RevokeOld raw token by its revoke URL: {base}/email-change/revoke#token={raw}.
        var revokeEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("email-change/revoke#token="),
            "the request endpoint issues exactly one RevokeOld email (revoke URL)").Which;
        var rawRevokeToken = AuthTestFixture.ExtractResetTokenFromMessage(revokeEmail);

        // Step 4: revoke under ceres_app (anonymous, token). A non-204 means the consume-write hit RLS
        // 42501 — the RevokeAsync gap. 204 confirms the BeginPreAuthUserScopeAsync covers the write.
        var revokeResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/email-change/revoke", new { token = rawRevokeToken });
        revokeResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "email-change revoke must persist under ceres_app");

        // Step 5: both token rows consumed AND the user's Email is UNCHANGED (revoke cancels the change).
        await using (var app = Factory.NewAppContext(_userId))
        {
            var consumed = await app.Context.EmailChangeTokens
                .CountAsync(t => t.UserId == _userId && t.ConsumedAt != null);
            consumed.Should().Be(2, "revoke consumes BOTH siblings (the RevokeOld + its VerifyNew)");

            var unchanged = await app.Context.Users
                .CountAsync(u => u.Id == _userId && u.Email == oldEmail);
            unchanged.Should().Be(1, "revoke must NOT mutate the owner's Email — the change is cancelled");

            var moved = await app.Context.Users
                .CountAsync(u => u.Id == _userId && u.Email == newEmail);
            moved.Should().Be(0, "revoke must leave the address-of-record on the OLD email");
        }

        // Step 6: positive + negative RLS control — both rows visible to owner under ceres_app, none to others.
        await AssertRlsVisibility<EmailChangeToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 2);
    }

    /// <summary>
    /// Sends an authenticated request with the session cookie + a user-bound CSRF pair, mirroring
    /// the cookie/header wiring MfaWritesUnderRlsTests uses for the [RequireRecentAuth] endpoints.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthedAsync(
        HttpClient client, string url, string sessionCookie, object body)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(Factory, _userId);
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return await client.SendAsync(req);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.EmailChangeTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
