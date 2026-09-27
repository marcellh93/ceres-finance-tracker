using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 13.9 Task 6b — D3's 30-day re-registration cooling-off, enforced at
/// AuthController.Register against ErasedEmailHolds. A held email must be
/// indistinguishable from a confirmed-duplicate email: same 204, no account created.
/// </summary>
[Collection("IntegrationParallel3")]
public class ErasedEmailHoldRegistrationTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;
    private readonly HttpClient _client;

    public ErasedEmailHoldRegistrationTests(Bucket3AuthFactory factory, Bucket3Database bucketDb)
        : base(factory, bucketDb)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        await admin.ErasedEmailHolds.IgnoreQueryFilters()
            .Where(h => h.UserId == _holderUserId)
            .ExecuteDeleteAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var u in userManager.Users.Where(u =>
            u.Email!.EndsWith("@erased-hold-test.local")).ToList())
        {
            await userManager.DeleteAsync(u);
        }
    }

    private readonly Guid _holderUserId = Guid.NewGuid();

    private async Task SeedHoldAsync(string email, DateTime erasedAt, DateTime expiresAt)
    {
        using var scope = _factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        admin.ErasedEmailHolds.Add(new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = _holderUserId,
            EmailFingerprint = hasher.ComputeLookup(email.ToUpperInvariant()),
            ErasedAt = erasedAt,
            ExpiresAt = expiresAt,
        });
        await admin.SaveChangesAsync();
    }

    [Fact]
    public async Task Register_with_live_held_email_returns_204_and_creates_no_account()
    {
        const string email = "held@erased-hold-test.local";
        var now = DateTime.UtcNow;
        await SeedHoldAsync(email, now.AddDays(-1), now.AddDays(29));

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a live hold must be indistinguishable from a confirmed-duplicate email");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().BeNull("a live hold must block account creation, not just report success");
    }

    [Fact]
    public async Task Register_with_expired_held_email_succeeds_and_creates_account()
    {
        const string email = "expired-held@erased-hold-test.local";
        var now = DateTime.UtcNow;
        await SeedHoldAsync(email, now.AddDays(-31), now.AddDays(-1));

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _client, "/api/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull("an expired hold must no longer block registration");
    }

    [Fact]
    public async Task Register_with_live_held_email_returns_same_body_shape_as_fresh_email()
    {
        var freshResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = "fresh@erased-hold-test.local", password = "correct horse battery staple" });
        freshResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var freshBody = await freshResp.Content.ReadAsStringAsync();

        const string heldEmail = "held-shape@erased-hold-test.local";
        var now = DateTime.UtcNow;
        await SeedHoldAsync(heldEmail, now.AddDays(-1), now.AddDays(29));

        var heldResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = heldEmail, password = "correct horse battery staple" });
        heldResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var heldBody = await heldResp.Content.ReadAsStringAsync();

        heldBody.Should().Be(freshBody,
            "a held email must return the identical response shape as a fresh registration (no enumeration)");
    }
}
