using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class LogoutWritesUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LogoutWritesUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Logout_under_ceres_app_revokes_session_and_writes_owner_only_audit_row()
    {
        // Register through the production HTTP endpoint: it owns its PreAuthUserScope, so the
        // category seed passes RLS under ceres_app. RegisterUserAsync seeds via the RLS-bound
        // DI context with no user scope and would itself fail 42501 on Categories (Task 4 trap).
        var email = $"logout-{Marker}@approle-test.local";
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm the email via BYPASSRLS admin context — login requires a confirmed account
        // (Program.cs SignIn.RequireConfirmedEmail = true). Seed-via-admin, act-via-app discipline.
        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        // Step 1: anonymous login. Returns the __Host-Session cookie value the authenticated
        // logout call carries forward. The session row write here is already proven safe under
        // ceres_app by Task 4 (LoginSessionWriteUnderRlsTests).
        var sessionCookie = await AuthTestFixture.LoginViaHttpAsync(Factory, client, email);
        sessionCookie.Should().NotBeNullOrEmpty();

        // Step 2: authenticated logout — session cookie + a CSRF token bound to the user.
        var (logoutCookie, logoutHeader) = AuthTestFixture.MintCsrf(Factory, _userId);
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        logoutReq.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={logoutCookie}");
        logoutReq.Headers.Add(SessionConstants.CsrfHeaderName, logoutHeader);
        var resp = await client.SendAsync(logoutReq);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "logout must revoke the session and write the Logout audit row under ceres_app — a 500 here " +
            "means a user-owned write hit RLS 42501 during the authenticated logout flow");

        // (a) The owner's ceres_app context sees its own session row, now revoked.
        await using (var app = Factory.NewAppContext(_userId))
        {
            var session = await app.Context.UserSessions.SingleAsync(s => s.UserId == _userId);
            session.RevokedAt.Should().NotBeNull("logout must stamp RevokedAt on the user's session row");
        }

        // (b) A Logout AuditLog row exists, visible to the owner only (AuditLogWriter self-scopes).
        await AssertRlsVisibility<AuditLog>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: a => a.UserId == _userId && a.Action == AuditLogAction.Logout, expectedOwnerCount: 1);
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
