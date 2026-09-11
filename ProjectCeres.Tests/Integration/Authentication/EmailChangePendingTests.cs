using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 12.8 — GET /api/auth/email-change/pending backs the settings banner that
/// tells a returning user a change is still in flight. Without it the settings page
/// shows the old address unchanged and the user cannot tell whether their request
/// worked, so they submit again and trigger two more emails.
/// </summary>
[Collection("IntegrationParallel1")]
public class EmailChangePendingTests : IntegrationTestBase<Bucket1AuthFactory>
{
    private readonly Bucket1AuthFactory _factory;

    public EmailChangePendingTests(Bucket1AuthFactory factory, Bucket1Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private async Task<HttpResponseMessage> GetPendingAsync(ApplicationUser user)
    {
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/email-change/pending");
        req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={cookie}");
        return await client.SendAsync(req);
    }

    private async Task<ApplicationUser> NewUserAsync(string marker) =>
        await AuthTestFixture.RegisterUserAsync(_factory, $"{marker}@pending-test.local");

    private async Task SeedTokenAsync(
        Guid userId, string newEmail, DateTime expiresAt, DateTime? consumedAt = null,
        EmailChangeTokenPurpose purpose = EmailChangeTokenPurpose.VerifyNew,
        DateTime? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmailChangeTokens.Add(new EmailChangeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            NewEmail = newEmail,
            TokenLookup = Guid.NewGuid().ToByteArray(),
            TokenHash = "unused-by-this-read-path",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            ExpiresAt = expiresAt,
            ConsumedAt = consumedAt,
        });
        await db.SaveChangesAsync(Timeout30s());
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Pending_reports_none_when_no_change_is_in_flight()
    {
        var user = await NewUserAsync($"none-{Guid.NewGuid():N}");

        var res = await GetPendingAsync(user);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await BodyAsync(res));
        data.GetProperty("pending").GetBoolean().Should().BeFalse();
        // The null-shape is the contract the not-yet-written banner binds against.
        data.GetProperty("maskedEmail").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("expired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Pending_reports_the_change_masked_never_in_full()
    {
        var user = await NewUserAsync($"live-{Guid.NewGuid():N}");
        await SeedTokenAsync(user.Id, "target@example.com", DateTime.UtcNow.AddMinutes(20));

        var res = await GetPendingAsync(user);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await res.Content.ReadAsStringAsync();
        // The whole point of masking server-side: the full address must never leave
        // the server, or devtools defeats the protection entirely.
        raw.Should().NotContain("target@example.com");
        raw.Should().NotContain("target");

        var data = JsonDocument.Parse(raw).RootElement;
        data.GetProperty("pending").GetBoolean().Should().BeTrue();
        data.GetProperty("maskedEmail").GetString().Should().Be(EmailMask.Mask("target@example.com"));
        data.GetProperty("expired").GetBoolean().Should().BeFalse();
        // The banner's "expires in N minutes" copy derives from this; asserting only
        // the derived `expired` flag would let the timestamp it comes from go missing.
        data.GetProperty("expiresAt").GetDateTime().Should()
            .BeCloseTo(DateTime.UtcNow.AddMinutes(20), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Pending_reports_an_expired_change_as_expired_rather_than_hiding_it()
    {
        // A user who missed the 30-minute window is exactly who needs telling; a
        // silently-empty banner reads as "nothing happened" and invites a resubmit
        // with no explanation.
        var user = await NewUserAsync($"exp-{Guid.NewGuid():N}");
        await SeedTokenAsync(user.Id, "late@example.com", DateTime.UtcNow.AddMinutes(-5));

        var res = await GetPendingAsync(user);

        var data = (await BodyAsync(res));
        data.GetProperty("pending").GetBoolean().Should().BeTrue();
        data.GetProperty("expired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Pending_ignores_a_consumed_token()
    {
        // Consumed means the change completed or was revoked — either way it is no
        // longer in flight and must not show as pending.
        var user = await NewUserAsync($"consumed-{Guid.NewGuid():N}");
        await SeedTokenAsync(user.Id, "done@example.com",
            DateTime.UtcNow.AddMinutes(20), consumedAt: DateTime.UtcNow);

        var res = await GetPendingAsync(user);

        (await BodyAsync(res)).GetProperty("pending").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    public async Task Pending_never_reports_another_users_change()
    {
        // The row is IUserOwned, so the query filter should scope it — this pins that
        // the endpoint reads through the filtered path and not a cross-tenant context.
        var mine = await NewUserAsync($"mine-{Guid.NewGuid():N}");
        var theirs = await NewUserAsync($"theirs-{Guid.NewGuid():N}");
        await SeedTokenAsync(theirs.Id, "notyours@example.com", DateTime.UtcNow.AddMinutes(20));

        // Positive control FIRST. Without it a typo in the seed, a rolled-back save, or
        // a Purpose mismatch leaves the negative assertion below green for the wrong
        // reason — the row simply never existed. Same discipline as the AppRole suite.
        var ownerRes = await GetPendingAsync(theirs);
        (await BodyAsync(ownerRes)).GetProperty("pending")
            .GetBoolean().Should().BeTrue("the owner must see their own pending change");

        var res = await GetPendingAsync(mine);

        var raw = await res.Content.ReadAsStringAsync();
        raw.Should().NotContain("notyours");
        JsonDocument.Parse(raw).RootElement.GetProperty("pending")
            .GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Pending_ignores_a_RevokeOld_token_even_though_it_is_unconsumed()
    {
        // The Purpose predicate is load-bearing and was untested. RevokeOld lives 7 days
        // against VerifyNew's 30 minutes, so without the filter a change would show as
        // pending for a WEEK after its real window closed — the exact stale-banner
        // failure this endpoint exists to prevent. Found by the 12.8 test audit.
        var user = await NewUserAsync($"revokeonly-{Guid.NewGuid():N}");
        await SeedTokenAsync(user.Id, "sibling@example.com", DateTime.UtcNow.AddDays(7),
            purpose: EmailChangeTokenPurpose.RevokeOld);

        var res = await GetPendingAsync(user);

        (await BodyAsync(res)).GetProperty("pending").GetBoolean()
            .Should().BeFalse("only the VerifyNew half decides whether a change is in flight");
    }

    [Fact]
    public async Task Pending_reports_the_newest_change_when_a_request_superseded_an_earlier_one()
    {
        // Ordering was untested: drop the OrderByDescending and the banner shows a
        // superseded address, which is its own kind of confusion.
        var user = await NewUserAsync($"superseded-{Guid.NewGuid():N}");
        await SeedTokenAsync(user.Id, "older@example.com", DateTime.UtcNow.AddMinutes(20),
            createdAt: DateTime.UtcNow.AddMinutes(-10));
        await SeedTokenAsync(user.Id, "newer@example.com", DateTime.UtcNow.AddMinutes(20),
            createdAt: DateTime.UtcNow);

        var res = await GetPendingAsync(user);

        var data = (await BodyAsync(res));
        data.GetProperty("maskedEmail").GetString().Should()
            .Be(EmailMask.Mask("newer@example.com"), "a re-request supersedes the earlier one");
    }

    [Fact]
    public async Task Pending_does_NOT_require_recent_auth_so_the_page_can_read_it_on_load()
    {
        // The absence of [RequireRecentAuth] is a deliberate design decision documented
        // in security-model.md, and nothing pinned it — every other test mints a FRESH
        // reauth stamp, so adding the attribute would not turn the suite red. To a
        // reviewer who has not read the bullet the omission looks like an oversight.
        var user = await NewUserAsync($"stale-{Guid.NewGuid():N}");
        var stale = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, stale);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/email-change/pending");
        req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={cookie}");

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.OK,
            "a stale reauth stamp must still read; the payload is masked, so no step-up is owed");
    }

    [Fact]
    public async Task Pending_requires_authentication()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var res = await client.GetAsync("/api/auth/email-change/pending", Timeout30s());

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
