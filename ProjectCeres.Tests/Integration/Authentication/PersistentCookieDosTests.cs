using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class PersistentCookieDosTests : IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;
    public PersistentCookieDosTests(AuthTestWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var u in um.Users.Where(u => u.Email!.EndsWith("@dos-test.local")).ToList())
        {
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.UserId == u.Id).ExecuteDeleteAsync();
            // Purge owned rows first: deleting the user cascades nothing.
            await UserOwnedCleanup.PurgeUserAsync(db, u.Id);
            await um.DeleteAsync(u);
        }
    }

    [Fact]
    public async Task MalformedPersistentCookie_DoesNotRunArgon2idScan()
    {
        // Plant several active persistent sessions so a linear scan would have to verify many.
        // (Even 5 is enough to make the timing assertion meaningful.)
        for (int i = 0; i < 5; i++)
        {
            var u = await AuthTestFixture.RegisterUserAsync(_factory, $"victim{i}@dos-test.local");
            var loginClient = _factory.CreateClient();
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, loginClient, "/api/auth/login",
                new { email = u.Email, password = AuthTestFixture.ValidPassword, rememberMe = true });
            resp.EnsureSuccessStatusCode();
        }

        // Confirm we actually have N persistent sessions.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n = await db.UserSessions.IgnoreQueryFilters().Where(s => s.IsPersistent && s.RevokedAt == null).CountAsync();
            n.Should().BeGreaterThanOrEqualTo(5);
        }

        // Send an unauthenticated request with a JUNK __Host-Persist cookie.
        // Junk = no separator; OR separator with non-base64url first half; OR random bytes that
        // parse but don't match any session id.
        var junkClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var junkReq = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        junkReq.Headers.Add("Cookie", $"{SessionConstants.PersistentCookieName}=garbage-no-separator");

        var sw = Stopwatch.StartNew();
        var junkResp = await junkClient.SendAsync(junkReq);
        sw.Stop();

        // Pre-fix: ~5 × 50ms Argon2id verifies = 250ms+. Post-fix: short-circuit on parse, ~10ms.
        // Use a generous bound (well below 5×argon-floor but well above no-op).
        sw.ElapsedMilliseconds.Should().BeLessThan(150,
            $"malformed cookie must short-circuit before the Argon2id scan; took {sw.ElapsedMilliseconds}ms");

        // Either 401 from the GET (no auth) or 200 if the endpoint is anonymous — either is fine.
        // The request must NOT have hung on the scan.
        ((int)junkResp.StatusCode).Should().BeOneOf([200, 401, 403]);
    }
}
