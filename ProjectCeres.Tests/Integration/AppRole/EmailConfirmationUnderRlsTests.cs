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
/// Stage 9.5d Task 8 — the symbolically key test. EmailConfirmationTokens is the exact table
/// whose MISSING RLS policy shipped a cross-user data-visibility bug in Stage 9.3, because no
/// test ever ran the verify flow under the restricted ceres_app role. THIS is the test that
/// would have caught it. EmailConfirmationService.ConfirmAsync looks the row up via the
/// BYPASSRLS admin context (TokenLookup), then opens its OWN BeginPreAuthUserScopeAsync keyed
/// to the resolved UserId on _db (ceres_app) so the consume-write + EmailConfirmed flip pass
/// user_isolation as the owner — expected to PASS with no production change. A 42501 on a flow
/// WRITE here would re-surface the 9.3 gap class and is a significant finding to escalate.
/// </summary>
[Collection("AppRoleTests")]
public class EmailConfirmationUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public EmailConfirmationUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task EmailConfirmation_verify_under_ceres_app_consumes_token_visible_only_to_owner()
    {
        // Capture the raw confirmation token by mocking IEmailService — the token is delivered
        // only by email (LogOnlyEmailService logs it; it is not returned in any HTTP response).
        // Fork the shared ceres_app-wired factory via WithWebHostBuilder so the fork preserves
        // UseAppRoleConnection => true; the HTTP flow runs on the fork (under ceres_app) while
        // seed/assert run on the original Factory (same project_ceres_test DB, shared state).
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

        // Step 1: register via the production HTTP endpoint. Register itself issues the single
        // EmailConfirmationToken row AND sends the confirmation email (captured). It owns its
        // PreAuthUserScope, so the category seed passes RLS under ceres_app.
        var email = $"emailconfirm-{Marker}@approle-test.local";
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Step 2: extract the raw token from the confirmation email. Register emits exactly one
        // message and its URL is of the form {base}/email-verify#token={raw}; select by that
        // URL and reuse the public ExtractResetTokenFromMessage helper (generic "token=" marker).
        var confirmEmail = captured.Should().ContainSingle(m => m.BodyText.Contains("email-verify#token="),
            "register issues exactly one confirmation email").Which;
        var rawToken = AuthTestFixture.ExtractResetTokenFromMessage(confirmEmail);

        // Step 3: capture the new user's id via admin context. The user is email-UNconfirmed at
        // this point — that's the whole point; verify is what flips it.
        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed.Should().BeFalse("precondition: registration leaves EmailConfirmed=false");
        }

        // Step 4: verify under ceres_app. A 500 here means the ConsumedAt write / EmailConfirmed
        // flip hit RLS 42501 — i.e. the 9.3 gap class — so a non-204 is the escalation signal.
        var verifyResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            httpFactory, client, "/api/auth/email/verify", new { token = rawToken });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "verify must consume the token + flip EmailConfirmed under ceres_app — a 500 here means the write hit " +
            "RLS 42501, i.e. EmailConfirmationService's own BeginPreAuthUserScopeAsync did not cover the consume/flip");

        // Step 5: the token is consumed AND the email is confirmed, seen via the owner's ceres_app context.
        await using (var app = Factory.NewAppContext(_userId))
        {
            var consumed = await app.Context.EmailConfirmationTokens
                .CountAsync(t => t.UserId == _userId && t.ConsumedAt != null);
            consumed.Should().BeGreaterThanOrEqualTo(1, "the verify flow must stamp ConsumedAt on the token row");

            var confirmed = await app.Context.Users
                .CountAsync(u => u.Id == _userId && u.EmailConfirmed);
            confirmed.Should().Be(1, "verify must flip EmailConfirmed on the owner's user row");
        }

        // Step 6: positive + negative RLS control — exactly one token row for this user, visible
        // only to the owner under ceres_app. Register issues exactly one token; verify consumes
        // (updates) that same row rather than adding another. This is the assertion that pins the
        // 9.3 gap shut: a missing RLS policy would let a different user's context see the row.
        await AssertRlsVisibility<EmailConfirmationToken>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: t => t.UserId == _userId, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.EmailConfirmationTokens.IgnoreQueryFilters().Where(t => t.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Categories.IgnoreQueryFilters().Where(c => c.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
