using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.AppRole;
using Xunit;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("AppRoleTests")]
public class SessionLoginDedupTests : AppRoleTestBase
{
    private Guid _userId;

    public SessionLoginDedupTests(AppRoleFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Second_login_from_the_same_device_revokes_the_first_session()
    {
        var marker = $"dedup-{Guid.NewGuid():N}";
        var email = $"{marker}@approle-test.local";
        // HandleCookies: false — PostJsonWithCsrfAsync manually attaches its own
        // Cookie header for the CSRF pair on every call. With the default jar-backed
        // client, login #1's Set-Cookie (__Host-XSRF, __Host-Session) would sit in the
        // jar and get auto-attached on login #2 alongside the manually-added Cookie
        // header, producing a duplicated Cookie header the antiforgery filter can't
        // parse (400, no ModelState detail). Login is anonymous, so no session cookie
        // is needed across calls anyway — this mirrors the SessionsApiTests pattern
        // for any client that logs in more than once.
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        // Register via the HTTP endpoint (owns its PreAuthUserScope), confirm the email
        // via admin context (login requires a confirmed account) — the exact seam
        // LoginSessionWriteUnderRlsTests uses.
        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        await using (var admin = Factory.NewAdminContext())
        {
            var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            _userId = u.Id; u.EmailConfirmed = true; await admin.Context.SaveChangesAsync();
        }

        // Two logins, identical UA + IP (both "" on the TestServer host).
        async Task Login() => (await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        await Login();
        await Login();

        await using var verify = Factory.NewAdminContext();
        var rows = await verify.Context.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == _userId).ToListAsync();

        rows.Count(s => s.RevokedAt == null && !s.IsPersistent).Should().Be(1,
            "the same device keeps exactly one live ephemeral session");
        rows.Count(s => s.RevokedAt != null).Should().BeGreaterThanOrEqualTo(1,
            "the superseded session survives as a revoked row for audit");
    }

    [Fact]
    public async Task Second_persistent_login_from_the_same_device_revokes_the_first_session()
    {
        var marker = $"dedup-persist-{Guid.NewGuid():N}";
        var email = $"{marker}@approle-test.local";
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        await using (var admin = Factory.NewAdminContext())
        {
            var u = await admin.Context.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            _userId = u.Id; u.EmailConfirmed = true; await admin.Context.SaveChangesAsync();
        }

        // rememberMe: true — the persistent path. Rotation cannot reach a superseded
        // persistent row (the browser holds one cookie), so login must revoke it.
        async Task Login() => (await AuthTestFixture.PostJsonWithCsrfAsync(Factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = true }))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
        await Login();
        await Login();

        await using var verify = Factory.NewAdminContext();
        var rows = await verify.Context.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == _userId).ToListAsync();

        rows.Count(s => s.RevokedAt == null && s.IsPersistent).Should().Be(1,
            "the same device keeps exactly one live persistent session");
        // The superseded row must not keep a usable token: a live hash on an
        // unreachable row would stay valid for the full 30-day persistent lifetime.
        rows.Where(s => s.RevokedAt == null).Should().HaveCount(1,
            "no session from this device survives the second login except the newest");
    }

    public override async Task DisposeAsync()
    {
        await using var admin = Factory.NewAdminContext();
        await admin.Context.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.AuditLogs.IgnoreQueryFilters().Where(a => a.UserId == _userId).ExecuteDeleteAsync();
        await admin.Context.Users.IgnoreQueryFilters().Where(u => u.Id == _userId).ExecuteDeleteAsync();
    }
}
