using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class EmailChangeRevokeTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeRevokeTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private async Task<(WebApplicationFactory<Program> Factory, ApplicationUser User,
                       string OldEmail, string NewEmail,
                       string VerifyToken, string RevokeToken)>
        ArrangePendingChangeAsync(List<EmailMessage> captured)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/request")
        {
            Content = JsonContent.Create(new { newEmail }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To.Address == newEmail));
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To.Address == oldEmail));

        captured.Clear();
        return (factory, user, oldEmail, newEmail, verifyToken, revokeToken);
    }

    private static async Task<HttpResponseMessage> PostRevokeAsync(
        WebApplicationFactory<Program> factory, AuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/revoke")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    // ── Test #19 ────────────────────────────────────────────────────────────
    // CRITICAL: revoke leaves user.Email UNCHANGED. The cancel path must not
    // touch the address-of-record under any circumstance.
    [Fact]
    public async Task Revoke_happy_path_leaves_user_Email_UNCHANGED()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um.FindByIdAsync(arr.User.Id.ToString())
            ?? throw new InvalidOperationException("user vanished");
        refreshed.Email.Should().Be(arr.OldEmail,
            "REVOKE MUST NOT MUTATE user.Email — the change is being cancelled");
        refreshed.NormalizedEmail.Should().Be(um.NormalizeEmail(arr.OldEmail));
        refreshed.UserName.Should().Be(arr.OldEmail);
    }

    // ── Test #20 ────────────────────────────────────────────────────────────
    // Sibling VerifyNew row consumed atomically with the matched RevokeOld.
    [Fact]
    public async Task Revoke_consumes_sibling_VerifyNew_row()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.ConsumedAt != null,
            "both Verify and Revoke siblings must be consumed");
    }

    // ── Test #21 ────────────────────────────────────────────────────────────
    // Negative-assertion: revoke does NOT revoke sessions and does NOT regen
    // SecurityStamp. Revoke is a cancel-pending-change, not a security event.
    [Fact]
    public async Task Revoke_does_NOT_revoke_sessions_and_does_NOT_regenerate_security_stamp()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        string stampBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await um.FindByIdAsync(arr.User.Id.ToString());
            stampBefore = u!.SecurityStamp ?? "";
        }

        var resp = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());
        sessions.Should().NotBeEmpty();
        sessions.Should().OnlyContain(s => s.RevokedAt == null,
            "revoke must NOT touch UserSession rows");

        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um2.FindByIdAsync(arr.User.Id.ToString());
        refreshed!.SecurityStamp.Should().Be(stampBefore,
            "revoke must NOT regenerate SecurityStamp");
    }

    // ── Test #22 ────────────────────────────────────────────────────────────
    // Negative: invalid token → 401.
    [Fact]
    public async Task Revoke_with_invalid_token_returns_401()
    {
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        var resp = await PostRevokeAsync(factory, _factory, "not-a-real-token");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #23 ────────────────────────────────────────────────────────────
    // Negative: expired token (8 days past) → 401. Insert directly with past ExpiresAt.
    [Fact]
    public async Task Revoke_with_expired_token_returns_401()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-rexpired-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-rexpired-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var generator = scope.ServiceProvider.GetRequiredService<EmailChangeTokenGenerator>();
            var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
            rawToken = generator.Generate();
            db.EmailChangeTokens.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = EmailChangeTokenPurpose.RevokeOld,
                NewEmail = newEmail,
                TokenLookup = lookupHasher.ComputeLookup(rawToken),
                TokenHash = generator.Hash(rawToken),
                CreatedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddDays(-1),
                ConsumedAt = null,
            });
            await db.SaveChangesAsync(Timeout30s());
        }

        var resp = await PostRevokeAsync(factory, _factory, rawToken);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #24 ────────────────────────────────────────────────────────────
    // Negative: already-consumed token → 401.
    [Fact]
    public async Task Revoke_with_already_consumed_token_returns_401()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var first = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    // ── Test #25 ────────────────────────────────────────────────────────────
    // Email sent to OLD address only — NOT to the new address.
    [Fact]
    public async Task Revoke_sends_email_to_OLD_address_only()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostRevokeAsync(factory, _factory, arr.RevokeToken);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        captured.Should().HaveCount(1, "exactly one notification, addressed to the old address");
        captured.Single().To.Address.Should().Be(arr.OldEmail);
        captured.Single().Subject.Should().Contain("cancelled");
    }
}
