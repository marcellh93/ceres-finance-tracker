using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Admin;

[Collection("IntegrationTests")]
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
}
