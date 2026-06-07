using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

/// <summary>
/// Stage 9.5d Task 7. Password-reset confirm is PRE-AUTH — no authenticated principal; the
/// raw token grants authority. PasswordResetService.ConfirmAsync looks the row up via the
/// BYPASSRLS admin context (TokenLookup), then opens its OWN BeginPreAuthUserScopeAsync keyed
/// to the resolved UserId on _db (ceres_app) so the consume-write passes user_isolation as the
/// owner. That self-scope is what makes the flow safe under ceres_app — expected to PASS with
/// no production change. Mechanism contrast with login: login is authenticated and relies on
/// HttpContext.User; here the service's own scope is the guarantor.
/// </summary>
[Collection("AppRoleTests")]
public class PasswordResetConfirmUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public PasswordResetConfirmUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task PasswordReset_confirm_under_ceres_app_consumes_token_visible_only_to_owner()
    {
        // Capture the raw reset token by mocking IEmailService, exactly as
        // PasswordResetConfirmNoMfaTests does — the token is delivered only by email
        // (LogOnlyEmailService logs it; it is not returned in any HTTP response). Fork the
        // shared ceres_app-wired factory via WithWebHostBuilder so the fork preserves
        // UseAppRoleConnection => true; the HTTP flow runs on the fork while seed/assert run
        // on the original Factory (same project_ceres_test DB, shared state).
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

        // Step 1: seed a confirmed non-MFA user. Register via the production HTTP endpoint
        // (it owns its PreAuthUserScope, so the category seed passes RLS under ceres_app —
        // RegisterUserAsync would itself trip 42501 on Categories, the Task 4 trap), then
        // confirm the email via the BYPASSRLS admin context.
        var email = $"pwreset-{Marker}@approle-test.local";
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        // Step 2: request a reset → exactly one PasswordResetToken row issued, raw token in the
        // email. Register emits a "Confirm your email" message first, so select the reset email
        // by its password-reset URL rather than by index.
        var requestResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/password-reset/request", new { email });
        requestResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var resetEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("password-reset#token="),
            "the request endpoint issues exactly one reset email").Which;
        var rawToken = AuthTestFixture.ExtractResetTokenFromMessage(resetEmail);

        // Step 3: confirm with a fresh, policy-passing password (≥15 chars, not pwned).
        var confirmResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/password-reset/confirm",
            new { token = rawToken, newPassword = "An0ther!ValidPassphrase" });
        confirmResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "confirm must consume the token under ceres_app — a 500 here means the ConsumedAt write hit RLS 42501, " +
            "i.e. PasswordResetService's own BeginPreAuthUserScopeAsync did not cover the consume update");

        // Step 4: the token is consumed, seen via the owner's ceres_app context.
        await using (var app = Factory.NewAppContext(_userId))
        {
            var consumed = await app.Context.PasswordResetTokens
                .CountAsync(t => t.UserId == _userId && t.ConsumedAt != null);
            consumed.Should().BeGreaterThanOrEqualTo(1, "the confirm flow must stamp ConsumedAt on the token row");
        }

        // Step 5: positive + negative RLS control — exactly one token row for this user, visible
        // only to the owner under ceres_app. One request issues one row, and the confirm flow
        // consumes (updates) that same row rather than adding another.
        await AssertRlsVisibility<PasswordResetToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.PasswordResetTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
