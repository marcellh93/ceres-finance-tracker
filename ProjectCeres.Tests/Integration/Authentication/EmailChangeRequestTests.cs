using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
public class EmailChangeRequestTests : IClassFixture<AuthTestWebApplicationFactory>, IAsyncLifetime
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeRequestTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => AuthTestTokenCleanup.DeleteAllTestTokensAsync(_factory);

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    /// <summary>Mints a fresh authenticated cookie for the user with a current LastReauthAt
    /// claim (so the [RequireRecentAuth] gate passes). Returns the cookie value.</summary>
    private async Task<string> MintFreshAuthCookieAsync(ApplicationUser user)
    {
        var fresh = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, fresh);
    }

    private async Task<HttpResponseMessage> PostRequestAsync(
        WebApplicationFactory<Program> factory, ApplicationUser user, string newEmail)
    {
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
        return await client.SendAsync(req);
    }

    // ── Test #1 ─────────────────────────────────────────────────────────────
    // Happy path: returns 202; persists exactly two EmailChangeToken rows
    // (one VerifyNew, one RevokeOld) sharing UserId + NewEmail; sends two emails
    // (one to new address, one to old address) with distinct raw tokens.
    [Fact]
    public async Task Request_happy_path_returns_202_and_persists_two_token_rows_and_sends_two_emails()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var resp = await PostRequestAsync(factory, user, newEmail);

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(2, "exactly one VerifyNew + one RevokeOld row per request");
        rows.Should().Contain(r => r.Purpose == EmailChangeTokenPurpose.VerifyNew);
        rows.Should().Contain(r => r.Purpose == EmailChangeTokenPurpose.RevokeOld);
        rows.Should().OnlyContain(r => r.NewEmail == newEmail);
        rows.Should().OnlyContain(r => r.ConsumedAt == null);

        var verify = rows.Single(r => r.Purpose == EmailChangeTokenPurpose.VerifyNew);
        verify.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(29));
        verify.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(31));

        var revoke = rows.Single(r => r.Purpose == EmailChangeTokenPurpose.RevokeOld);
        revoke.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));
        revoke.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(8));

        captured.Should().HaveCount(2, "one verify email to new address + one revoke email to old address");
        captured.Should().Contain(m => m.To == newEmail && m.Subject.Contains("Confirm"));
        captured.Should().Contain(m => m.To == oldEmail && m.Subject.Contains("change was requested"));

        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == newEmail));
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == oldEmail));
        verifyToken.Should().NotBe(revokeToken, "verify and revoke tokens are independent 256-bit values");
    }

    // ── Test #2 ─────────────────────────────────────────────────────────────
    // Negative: stale LastReauthAt claim → 401 REAUTH_REQUIRED. No token rows
    // are written. Pins that the [RequireRecentAuth] gate fires before the
    // service is reached.
    [Fact]
    public async Task Request_without_recent_reauth_returns_401_REAUTH_REQUIRED_and_writes_no_rows()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-stale-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-stale-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var stale = DateTimeOffset.UtcNow.AddSeconds(-301).ToUnixTimeSeconds();
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, stale);
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

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id)
            .CountAsync(Timeout30s());
        rows.Should().Be(0, "stale-reauth request must not write any token rows");
        captured.Should().BeEmpty("stale-reauth request must not send emails");
    }

    // ── Test #3 ─────────────────────────────────────────────────────────────
    // Negative: newEmail already registered to another user → 422 EMAIL_ALREADY_IN_USE.
    // No rows persisted, no emails sent.
    [Fact]
    public async Task Request_with_newEmail_already_registered_returns_422_EMAIL_ALREADY_IN_USE_and_writes_no_rows()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-collision-{Guid.NewGuid():N}@example.com";
        var collisionEmail = $"taken-{Guid.NewGuid():N}@example.com";

        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);
        await AuthTestFixture.RegisterUserAsync(_factory, collisionEmail);

        var resp = await PostRequestAsync(factory, user, collisionEmail);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("EMAIL_ALREADY_IN_USE");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id)
            .CountAsync(Timeout30s());
        rows.Should().Be(0);
        captured.Should().BeEmpty();
    }

    // ── Test #4 ─────────────────────────────────────────────────────────────
    // Negative: newEmail equals current address (case-insensitive against
    // NormalizedEmail) → 422 EMAIL_UNCHANGED. No rows persisted.
    [Fact]
    public async Task Request_with_newEmail_equal_to_current_returns_422_EMAIL_UNCHANGED_and_writes_no_rows()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"unchanged-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        // Submit the same email with a case variation to confirm normalize-compare.
        var resp = await PostRequestAsync(factory, user, oldEmail.ToUpperInvariant());

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("EMAIL_UNCHANGED");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id)
            .CountAsync(Timeout30s());
        rows.Should().Be(0);
        captured.Should().BeEmpty();
    }

    // ── Test #5 ─────────────────────────────────────────────────────────────
    // Supersession: a second /request from the same user marks the first
    // request's two rows as consumed and inserts two fresh rows.
    [Fact]
    public async Task Second_request_supersedes_first_marking_first_two_rows_consumed_and_inserts_two_fresh_rows()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-supersede-{Guid.NewGuid():N}@example.com";
        var firstNewEmail = $"first-{Guid.NewGuid():N}@example.com";
        var secondNewEmail = $"second-{Guid.NewGuid():N}@example.com";

        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var first = await PostRequestAsync(factory, user, firstNewEmail);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await PostRequestAsync(factory, user, secondNewEmail);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(4, "two pairs persisted total: first pair superseded, second pair active");

        var firstPair = rows.Where(r => r.NewEmail == firstNewEmail).ToList();
        firstPair.Should().HaveCount(2);
        firstPair.Should().OnlyContain(r => r.ConsumedAt != null,
            "the second /request must have consumed both of the first request's rows");

        var secondPair = rows.Where(r => r.NewEmail == secondNewEmail).ToList();
        secondPair.Should().HaveCount(2);
        secondPair.Should().OnlyContain(r => r.ConsumedAt == null);
    }

    // ── Test #9 ─────────────────────────────────────────────────────────────
    // Negative-assertion (per feedback_test_edge_cases_as_ship_gate): a request
    // submitted with a valid session cookie but NO LastReauthAt claim in the
    // ticket must still 401 REAUTH_REQUIRED. The reauth gate is independent of
    // session validity. (Distinct from #2 which sets a stale-but-present claim.)
    [Fact]
    public async Task Request_does_NOT_bypass_reauth_with_valid_session_cookie()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-noreauth-{Guid.NewGuid():N}@example.com";
        var newEmail = $"new-noreauth-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        // Mint a cookie with NO LastReauthAt claim at all.
        var cookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);
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

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
        captured.Should().BeEmpty();
    }
}
