using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration.AppRole;

[Collection("AppRoleTests")]
public class LoginSessionWriteUnderRlsTests : AppRoleTestBase
{
    private Guid _userId;

    public LoginSessionWriteUnderRlsTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Login_under_ceres_app_persists_a_UserSession_visible_only_to_its_owner()
    {
        // Register through the production HTTP endpoint: it owns its PreAuthUserScope, so
        // the category seed passes RLS under ceres_app. RegisterUserAsync seeds via the
        // RLS-bound DI context with no user scope and would itself fail 42501 on Categories.
        var email = $"login-{Marker}@approle-test.local";
        var client = Factory.CreateClient();
        var register = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/register", new { email, password = AuthTestFixture.ValidPassword });
        register.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Confirm the email via BYPASSRLS admin context — login requires a confirmed account
        // (Program.cs SignIn.RequireConfirmedEmail = true). Seed-via-admin, act-via-app discipline.
        await using (var admin = Factory.NewAdminContext())
        {
            var seeded = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            _userId = seeded.Id;
            seeded.EmailConfirmed = true;
            await admin.Context.SaveChangesAsync();
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            Factory, client, "/api/auth/login", new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent,
            "login must persist the session row under ceres_app — a 500 here means the UserSessions insert hit RLS 42501 " +
            "(the post-PasswordSignInAsync HttpContext.User no longer resolving app.current_user_ref before the insert)");

        await AssertRlsVisibility<UserSession>(
            owner: _userId, otherUser: Guid.NewGuid(),
            predicate: s => s.UserId == _userId && s.RevokedAt == null, expectedOwnerCount: 1);
    }

    [Fact]
    public async Task UserSession_insert_under_ceres_app_with_unowned_UserId_is_rejected_by_RLS()
    {
        // Mechanism pin (sibling to the test above): this is WHY login must resolve the
        // principal before the session write. NewAppContext(Guid.Empty) enters the RLS
        // scope with no real user, so the GUC app.current_user_ref does not match the
        // row's UserId; the user_isolation WITH CHECK rejects the INSERT with 42501.
        // Login succeeds only because SignInManager populates HttpContext.User first,
        // which sets the GUC to the owner before the UserSessions insert fires.
        var orphanUserId = Guid.NewGuid();
        var session = new UserSession
        {
            Id           = Guid.NewGuid(),
            UserId       = orphanUserId,
            IpCreatedAt  = "127.0.0.1",
            UserAgent    = "approle-mechanism-test",
            CreatedAt    = DateTime.UtcNow,
            LastUsedAt   = DateTime.UtcNow,
            IsPersistent = false,
        };

        await using var app = Factory.NewAppContext(Guid.Empty); // no real user resolved
        app.Context.UserSessions.Add(session);

        var act = async () => await app.Context.SaveChangesAsync();

        var ex = await act.Should().ThrowAsync<RlsPolicyViolationException>(
            "RLS must reject a UserSession insert when the acting context owns no matching GUC");
        ex.Which.TableName.Should().Be("UserSessions");
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
