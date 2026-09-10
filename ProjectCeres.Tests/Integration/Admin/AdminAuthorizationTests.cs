using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Controllers.Api;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

[Collection("IntegrationParallel3")]
public class AdminAuthorizationTests : IAsyncLifetime
{
    private const string EmailSuffix = "@admin-authz-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public AdminAuthorizationTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    private async Task<(HttpClient Client, string Session, ApplicationUser User)> SignedInAsync(string email)
    {
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, session, user);
    }

    private static HttpRequestMessage Promote(Guid targetId, string session, string csrfCookie, string csrfHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/promote");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return req;
    }

    private static HttpRequestMessage Demote(Guid targetId, string session, string csrfCookie, string csrfHeader)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{targetId}/demote");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return req;
    }

    [Fact]
    public async Task Anonymous_is_refused()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{Guid.NewGuid()}/promote");
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_non_admin_is_refused()
    {
        var (client, session, caller) = await SignedInAsync($"plain{EmailSuffix}");
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);

        var resp = await client.SendAsync(Promote(Guid.NewGuid(), session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "holding a session is not the same as holding the Admin role");
    }

    [Fact]
    public async Task An_admin_can_promote_another_account()
    {
        var (client, session, caller) = await SignedInAsync($"caller{EmailSuffix}");
        var target = await AuthTestFixture.RegisterUserAsync(_factory, $"target{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            (await svc.IsAdminAsync(target.Id)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Promoting_an_unknown_account_is_a_404()
    {
        var (client, session, caller) = await SignedInAsync($"unknown{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Promote(Guid.NewGuid(), session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Demoting_a_non_admin_that_exists_is_a_204()
    {
        var (client, session, caller) = await SignedInAsync($"demote-plain{EmailSuffix}");
        var target = await AuthTestFixture.RegisterUserAsync(_factory, $"demote-target{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Demote(target.Id, session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the target exists and is already not an admin — the caller's goal is met, " +
            "and reporting 404 would claim a real account does not exist");
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted()
    {
        var (client, session, caller) = await SignedInAsync($"last-admin{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Demote(caller.Id, session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "removing the only admin locks every admin surface, including the promote " +
            "endpoint that would undo it");

        using var check = _factory.Services.CreateScope();
        var svc2 = check.ServiceProvider.GetRequiredService<AdminRoleService>();
        (await svc2.IsAdminAsync(caller.Id)).Should().BeTrue("the refused demote must not have changed state");
    }

    [Fact]
    public async Task An_admin_can_step_down_once_another_admin_exists()
    {
        var (client, session, caller) = await SignedInAsync($"stepdown{EmailSuffix}");
        var successor = await AuthTestFixture.RegisterUserAsync(_factory, $"successor{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
            await svc.GrantAsync(successor.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var resp = await client.SendAsync(Demote(caller.Id, session, csrfCookie, csrfHeader));

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "stepping down after promoting a colleague is the intended handover");

        using var check = _factory.Services.CreateScope();
        var svc2 = check.ServiceProvider.GetRequiredService<AdminRoleService>();
        (await svc2.IsAdminAsync(caller.Id)).Should().BeFalse();
    }

    /// <summary>
    /// The admin gate must be declared once at the class level, not repeated per action.
    /// A per-action check is satisfiable by omission — a future action that simply forgets
    /// it is admin-only in name and authenticated-only in fact, and no build or
    /// architecture test would notice the missing lines.
    /// </summary>
    [Fact]
    public void Every_action_on_the_admin_controller_is_gated_by_the_class_level_policy()
    {
        var controller = typeof(AdminUsersApiController);

        controller.GetCustomAttributes(typeof(RequireAdminAttribute), inherit: true)
            .Should().NotBeEmpty(
                "the gate must sit on the class so it covers every present and future action");

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        actions.Should().NotBeEmpty("the controller must expose at least one action to be worth gating");

        foreach (var action in actions)
        {
            action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
                .Should().BeEmpty(
                    $"{action.Name} would punch a hole in the class-level admin gate");
        }
    }

    /// <summary>
    /// Pins that the gate reads role membership live rather than from the auth cookie.
    /// The caller signs in with no role, is granted Admin afterwards, and must be
    /// admitted on the very next request without re-authenticating.
    /// </summary>
    [Fact]
    public async Task A_role_granted_after_sign_in_takes_effect_without_re_login()
    {
        var (client, session, caller) = await SignedInAsync($"midsession{EmailSuffix}");
        var target = await AuthTestFixture.RegisterUserAsync(_factory, $"midsession-target{EmailSuffix}");

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var before = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));
        before.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no role yet");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
        }

        var after = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));

        after.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the grant must be visible on the same session — a claims-based check would " +
            "keep returning 403 until the cookie was reissued at next sign-in");
    }

    /// <summary>
    /// The mirror of the grant case, and the reason ADR-0080 accepts the live check over
    /// [Authorize(Roles = ...)]: a revoked admin must lose access on the very next request,
    /// not when their cookie eventually expires. A gate that cached the first lookup per
    /// session would pass every other test in this class.
    /// </summary>
    [Fact]
    public async Task A_role_revoked_after_sign_in_takes_effect_immediately()
    {
        var (client, session, caller) = await SignedInAsync($"revoked{EmailSuffix}");
        var keeper = await AuthTestFixture.RegisterUserAsync(_factory, $"revoked-keeper{EmailSuffix}");
        var target = await AuthTestFixture.RegisterUserAsync(_factory, $"revoked-target{EmailSuffix}");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.GrantAsync(caller.Id);
            await svc.GrantAsync(keeper.Id);   // so revoking the caller is not a last-admin 409
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, caller.Id);
        var before = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));
        before.StatusCode.Should().Be(HttpStatusCode.NoContent, "the caller is an admin at this point");

        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await svc.RevokeAsync(caller.Id);
        }

        var after = await client.SendAsync(Promote(target.Id, session, csrfCookie, csrfHeader));

        after.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the same session must lose admin access the moment the role is revoked — under " +
            "a claims-based check the stale role claim would stay valid until the cookie " +
            "expired, up to 30 days on a persistent session");
    }

    /// <summary>
    /// AdminCountAsync is only exercised transitively through the last-admin 409, where a
    /// handler returning a constant would still produce the expected status. This asserts
    /// the count itself moves with the number of admins.
    /// </summary>
    [Fact]
    public async Task AdminCountAsync_tracks_the_number_of_admins()
    {
        var first = await AuthTestFixture.RegisterUserAsync(_factory, $"count-one{EmailSuffix}");
        var second = await AuthTestFixture.RegisterUserAsync(_factory, $"count-two{EmailSuffix}");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AdminRoleService>();

        var baseline = await svc.AdminCountAsync();

        await svc.GrantAsync(first.Id);
        (await svc.AdminCountAsync()).Should().Be(baseline + 1);

        await svc.GrantAsync(second.Id);
        (await svc.AdminCountAsync()).Should().Be(baseline + 2);

        await svc.RevokeAsync(first.Id);
        (await svc.AdminCountAsync()).Should().Be(baseline + 1,
            "the count must fall when a role is revoked, not only rise when one is granted");
    }
}
