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
[Collection("IntegrationTests")]
public class EmailChangePendingTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangePendingTests(AuthTestWebApplicationFactory factory) => _factory = factory;

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
        Guid userId, string newEmail, DateTime expiresAt, DateTime? consumedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.EmailChangeTokens.Add(new EmailChangeToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = EmailChangeTokenPurpose.VerifyNew,
            NewEmail = newEmail,
            TokenLookup = Guid.NewGuid().ToByteArray(),
            TokenHash = "unused-by-this-read-path",
            CreatedAt = DateTime.UtcNow,
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
        var body = await BodyAsync(res);
        body.GetProperty("data").GetProperty("pending").GetBoolean().Should().BeFalse();
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

        var data = JsonDocument.Parse(raw).RootElement.GetProperty("data");
        data.GetProperty("pending").GetBoolean().Should().BeTrue();
        data.GetProperty("maskedEmail").GetString().Should().Be(EmailMask.Mask("target@example.com"));
        data.GetProperty("expired").GetBoolean().Should().BeFalse();
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

        var data = (await BodyAsync(res)).GetProperty("data");
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

        (await BodyAsync(res)).GetProperty("data").GetProperty("pending").GetBoolean()
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

        var res = await GetPendingAsync(mine);

        var raw = await res.Content.ReadAsStringAsync();
        raw.Should().NotContain("notyours");
        JsonDocument.Parse(raw).RootElement.GetProperty("data").GetProperty("pending")
            .GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Pending_requires_authentication()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var res = await client.GetAsync("/api/auth/email-change/pending", Timeout30s());

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
