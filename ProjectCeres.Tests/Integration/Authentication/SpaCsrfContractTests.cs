using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Pins the CSRF contract the SPA must satisfy on state-changing endpoints.
/// The WAF used by feature tests strips the global antiforgery filter, so those
/// suites cannot observe a missing X-XSRF-TOKEN header; these run the real pipeline
/// with a genuine authenticated session, which is where the SPA's 400 came from.
/// </summary>
[Collection("IntegrationParallel1")]
public class SpaCsrfContractTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private const string EmailSuffix = "@spa-csrf-test.local";
    private readonly AuthTestWebApplicationFactory _factory;

    public SpaCsrfContractTests(AuthTestWebApplicationFactory factory, Bucket1Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith(EmailSuffix)).ToList())
        {
            // Deleting the user cascades nothing — Categories/Accounts carry a bare
            // UserId with no FK to AspNetUsers — so purge the owned rows first or
            // registration's seeded categories are stranded permanently.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    private async Task<(HttpClient Client, string Session)> AuthenticatedClientAsync(string email)
    {
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, session);
    }

    /// <summary>
    /// Reproduces the Movements "cleared" pill failure. An authenticated PATCH carrying
    /// the __Host-XSRF cookie but no X-XSRF-TOKEN header is rejected by antiforgery with
    /// a 400 before reaching the controller — what the SPA rendered as
    /// "Couldn't update status."
    /// </summary>
    [Fact]
    public async Task Authenticated_patch_cleared_without_csrf_header_returns_400()
    {
        var (client, session) = await AuthenticatedClientAsync($"no-header{EmailSuffix}");
        var (csrfCookie, _) = AuthTestFixture.MintCsrf(_factory);

        var req = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/movements/{Guid.NewGuid()}/cleared")
        {
            Content = JsonContent.Create(new { type = "transaction", cleared = true }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an authenticated state-changing PATCH without X-XSRF-TOKEN must be rejected by " +
            "antiforgery — this is exactly what the SPA's raw fetch() calls were sending");
    }

    /// <summary>
    /// The same authenticated PATCH with a valid CSRF header clears antiforgery and
    /// reaches the controller, which returns 404 for an unknown movement id. Proves the
    /// 400 above came from antiforgery and not from model binding or the handler.
    /// </summary>
    [Fact]
    public async Task Authenticated_patch_cleared_with_csrf_header_reaches_controller()
    {
        var email = $"with-header{EmailSuffix}";
        await AuthTestFixture.RegisterUserAsync(_factory, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var session = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, user!.Id);

        var req = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/movements/{Guid.NewGuid()}/cleared")
        {
            Content = JsonContent.Create(new { type = "transaction", cleared = true }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={session}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "with a valid CSRF header the request must clear antiforgery and reach the " +
            "handler, which 404s on an unknown movement id");
    }
}
