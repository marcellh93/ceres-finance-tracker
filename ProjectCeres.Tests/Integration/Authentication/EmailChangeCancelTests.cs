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

/// <summary>
/// Stage 12.8.1 — in-app cancel of a pending email change (POST /api/auth/email-change/cancel).
/// Authenticated, NOT reauth-gated, NOT token-based: it consumes the caller's own pending token
/// pair. Same end state as the emailed revoke link, so it must match RevokeAsync's guarantees:
/// leaves user.Email UNCHANGED, consumes both siblings, and does NOT revoke sessions or regen
/// the SecurityStamp (a cancel is not a security event — only /confirm is).
/// </summary>
public class EmailChangeCancelTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeCancelTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // Arrange a pending change AND return an authenticated cookie for the user, since cancel is
    // an authenticated call (unlike revoke, which is token-based and anonymous).
    private async Task<(WebApplicationFactory<Program> Factory, ApplicationUser User, string OldEmail, string Cookie)>
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
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        captured.Clear();
        return (factory, user, oldEmail, cookie);
    }

    private async Task<HttpResponseMessage> PostCancelAsync(
        WebApplicationFactory<Program> factory, string cookie, Guid userId)
    {
        // CSRF bound to the authenticated user — cancel is [Authorize], and the antiforgery
        // request token must validate against the user the auth cookie carries (the anonymous
        // token the revoke tests use works only because /revoke is [AllowAnonymous]).
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, userId);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/cancel");
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={cookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task Cancel_consumes_both_sibling_tokens_and_leaves_user_Email_unchanged()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var resp = await PostCancelAsync(factory, arr.Cookie, arr.User.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == arr.User.Id).ToListAsync(Timeout30s());
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.ConsumedAt != null,
            "cancel must consume both the VerifyNew and RevokeOld siblings");
        // Name the pair explicitly (test-audit finding): count-2 + all-consumed would also pass
        // if the code consumed two rows of ONE purpose. Consumption keys on NewEmail, so a
        // "consumed the wrong sibling" bug is representable — pin that BOTH purposes are gone.
        rows.Select(r => r.Purpose).Should().BeEquivalentTo(
            new[] { EmailChangeTokenPurpose.VerifyNew, EmailChangeTokenPurpose.RevokeOld });

        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var refreshed = await um.FindByIdAsync(arr.User.Id.ToString());
        refreshed!.Email.Should().Be(arr.OldEmail, "cancel must NOT mutate the address of record");

        // The cancel writes an EmailChangeRevoked audit row (test-audit finding: this
        // production behaviour was previously unpinned).
        var audit = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.UserId == arr.User.Id && a.Action == AuditLogAction.EmailChangeRevoked)
            .ToListAsync(Timeout30s());
        audit.Should().ContainSingle("cancel records exactly one EmailChangeRevoked audit entry");
    }

    [Fact]
    public async Task Cancel_does_NOT_revoke_sessions_or_regenerate_security_stamp()
    {
        // Negative control (mirrors the revoke suite): cancelling a pending change is not a
        // security event. Only /confirm revokes sessions + rolls the stamp.
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        string stampBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            stampBefore = (await um.FindByIdAsync(arr.User.Id.ToString()))!.SecurityStamp ?? "";
        }

        (await PostCancelAsync(factory, arr.Cookie, arr.User.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope2 = factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stampAfter = (await um2.FindByIdAsync(arr.User.Id.ToString()))!.SecurityStamp ?? "";
        stampAfter.Should().Be(stampBefore, "cancel must not regenerate the SecurityStamp");
    }

    [Fact]
    public async Task Cancel_with_no_pending_change_is_still_204()
    {
        // A user with nothing in flight cancelling is a no-op with the desired end state.
        var oldEmail = $"nopending-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(
            _factory, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var resp = await PostCancelAsync(_factory, cookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cancel_succeeds_with_a_STALE_reauth_session()
    {
        // P6 positive control (test-audit finding): cancel is [Authorize], NOT [RequireRecentAuth].
        // A session whose last-reauth is well outside the 5-minute window must STILL cancel — that
        // is the whole point of not reauth-gating it. Pinned positively, not just by the gate's
        // absence, so a future [RequireRecentAuth] added here would fail this test.
        var captured = new List<EmailMessage>();
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));
        await using var _ = factory;

        var oldEmail = $"stale-{Guid.NewGuid():N}@example.com";
        var newEmail = $"stalenew-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        // Arrange the pending change with a FRESH cookie (request IS reauth-gated)...
        var freshCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(
            _factory, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, user.Id);
        var reqClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/request")
        {
            Content = JsonContent.Create(new { newEmail }),
        };
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={freshCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        (await reqClient.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        // ...then cancel with a STALE reauth timestamp (10 minutes ago, past the 5-min window).
        var staleCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(
            _factory, user, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds());
        var resp = await PostCancelAsync(factory, staleCookie, user.Id);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "cancel is authenticated-only; a stale reauth window must not block it");
    }

    [Fact]
    public async Task Cancel_requires_authentication()
    {
        // No session cookie → 401. Cancel is [Authorize].
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/cancel");
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
