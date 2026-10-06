using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

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

    public Task InitializeAsync() => AuthTestFixture.PurgeUsersByEmailSuffixAsync(_factory.Services, "@erased-hold-test.local");

    // This file's tests seed ErasedEmailHolds against fixed, hardcoded emails (not
    // per-test-unique ones), so EmailFingerprint is identical every run. Cleaning up
    // only by _holderUserId (a fresh Guid per test INSTANCE) leaves an orphaned row
    // whenever a run is interrupted before DisposeAsync executes — that row's fixed
    // fingerprint then collides with the next run's INSERT (unique constraint
    // IX_ErasedEmailHolds_EmailFingerprint), failing a test this file never touched.
    // Sweeping by fingerprint instead (every email ANY test in this file seeds) means
    // a crashed run self-heals on the next run, regardless of which test crashed.
    private static readonly string[] SeededEmails =
    [
        "held@erased-hold-test.local",
        "expired-held@erased-hold-test.local",
        "held-shape@erased-hold-test.local",
    ];

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();

        var fingerprints = SeededEmails
            .Select(email => hasher.ComputeLookup(normalizer.NormalizeEmail(email) ?? email))
            .ToArray();
        await admin.ErasedEmailHolds.IgnoreQueryFilters()
            .Where(h => fingerprints.Contains(h.EmailFingerprint))
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
        var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        admin.ErasedEmailHolds.Add(new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = _holderUserId,
            // Must match AuthController.IsEmailHeldAsync / ErasureExecutor.RecordEmailHoldAsync's
            // normalization exactly (the project's registered ILookupNormalizer, not a hand-rolled
            // case conversion) — a divergent normalizer means the seeded hold can never match.
            EmailFingerprint = hasher.ComputeLookup(normalizer.NormalizeEmail(email) ?? email),
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

    /// <summary>
    /// The real indistinguishability contract is held-vs-confirmed-duplicate, not
    /// held-vs-fresh: both fresh and held return an empty 204 body by definition,
    /// so comparing those two bodies is vacuous ("" == ""). A confirmed-duplicate
    /// email is the only OTHER branch that returns 204 without creating an account,
    /// so it is the meaningful thing a held email must be indistinguishable from.
    /// </summary>
    [Fact]
    public async Task Register_with_live_held_email_is_indistinguishable_from_confirmed_duplicate()
    {
        const string confirmedEmail = "confirmed-dup@erased-hold-test.local";
        await AuthTestFixture.RegisterUserAsync(_factory, confirmedEmail);

        var dupResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = confirmedEmail, password = "correct horse battery staple" });
        dupResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        const string heldEmail = "held-shape@erased-hold-test.local";
        var now = DateTime.UtcNow;
        await SeedHoldAsync(heldEmail, now.AddDays(-1), now.AddDays(29));

        var heldResp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, _factory.CreateClient(),
            "/api/auth/register",
            new { email = heldEmail, password = "correct horse battery staple" });
        heldResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        heldResp.StatusCode.Should().Be(dupResp.StatusCode,
            "a held email must return the identical status as a confirmed-duplicate email (no enumeration)");

        var dupHeaderNames = dupResp.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var heldHeaderNames = heldResp.Headers.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        heldHeaderNames.Should().BeEquivalentTo(dupHeaderNames,
            "a held email must return the identical header set as a confirmed-duplicate email (no enumeration)");
    }
}
