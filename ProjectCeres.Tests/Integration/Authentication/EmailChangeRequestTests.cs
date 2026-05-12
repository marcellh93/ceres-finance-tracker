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
public class EmailChangeRequestTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeRequestTests(AuthTestWebApplicationFactory factory) => _factory = factory;

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
            .IgnoreQueryFilters()
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

        // Stage 6.15 — both rows carry an HMAC-derived TokenLookup so /confirm and
        // /revoke find them in O(1). The two rows MUST have distinct lookups because
        // they wrap distinct raw tokens (one VerifyNew + one RevokeOld per /request).
        verify.TokenLookup.Should().NotBeNull().And.HaveCount(32);
        revoke.TokenLookup.Should().NotBeNull().And.HaveCount(32);
        verify.TokenLookup.Should().NotEqual(revoke.TokenLookup,
            "VerifyNew and RevokeOld must have distinct lookups so a raw token cannot match across purposes");

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
            .IgnoreQueryFilters()
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
            .IgnoreQueryFilters()
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
            .IgnoreQueryFilters()
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
            .IgnoreQueryFilters()
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

    // ── Stage 6.16 timing-channel regressions ───────────────────────────────
    // Pin that the EmailAlreadyInUse and EmailUnchanged fast-return branches pay
    // equivalent Argon2id cost to the happy path. Pre-6.16 both branches returned
    // without any Argon2id work, leaking ≈300ms (two Argon2id missed: the
    // FindByEmail-equalisation hash plus the two `_tokens.Hash` token-hash costs
    // on the happy path). The fix adds THREE RunDummyHash calls to each fast-return
    // branch. Threshold mirrors PasswordResetRequestTests.

    private async Task<long> MeasureRequest(
        WebApplicationFactory<Program> factory, ApplicationUser user, string newEmail)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await PostRequestAsync(factory, user, newEmail);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static double Median(List<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;
        if (n == 0) throw new InvalidOperationException("median of empty list");
        return n % 2 == 1 ? sorted[n / 2] : (sorted[(n / 2) - 1] + sorted[n / 2]) / 2.0;
    }

    [Fact]
    public async Task Request_for_email_already_in_use_has_same_timing_as_unknown_user_branch()
    {
        // Highest-severity timing channel: pre-6.16, an authenticated user could enumerate
        // OTHER users' email addresses by timing /email-change/request against guessed
        // addresses (≈300ms gap for the EmailAlreadyInUse fast-return path).
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        var requester = await AuthTestFixture.RegisterUserAsync(_factory, $"req-already-{Guid.NewGuid():N}@example.com");

        // Warm-up. Use a fresh occupier so the warm-up's EF cache state matches the
        // measurement loop's cold-miss-per-iteration pattern.
        var warmupUser = await AuthTestFixture.RegisterUserAsync(_factory, $"warm-already-{Guid.NewGuid():N}@example.com");
        await MeasureRequest(factory, warmupUser, $"warmup-unknown-{Guid.NewGuid():N}@example.com");
        var warmupOccupier = await AuthTestFixture.RegisterUserAsync(_factory, $"warm-occ-{Guid.NewGuid():N}@example.com");
        await MeasureRequest(factory, requester, warmupOccupier.Email!);

        // Measurement loop: a FRESH occupier per iteration so the EF identity-map
        // cache is cold-miss for both branches. Reusing one occupier across all
        // iterations would warm the cache after the first hit and make the
        // already-in-use branch artificially fast — that's an EF-cache artefact,
        // not a real timing-channel signal, and it caused the original 6.16 test
        // to fail intermittently under full-suite load.
        const int iterations = 7;
        var unknownTimings = new List<long>(iterations);
        var alreadyInUseTimings = new List<long>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var occupier = await AuthTestFixture.RegisterUserAsync(_factory, $"occ-{i}-{Guid.NewGuid():N}@example.com");
            unknownTimings.Add(await MeasureRequest(factory, requester, $"never-used-{i}-{Guid.NewGuid():N}@example.com"));
            alreadyInUseTimings.Add(await MeasureRequest(factory, requester, occupier.Email!));
        }

        var medianUnknown = Median(unknownTimings);
        var medianAlreadyInUse = Median(alreadyInUseTimings);
        var diffMs = Math.Abs(medianUnknown - medianAlreadyInUse);

        diffMs.Should().BeLessThan(125,
            "Stage 6.16: EmailAlreadyInUse branch must pay equivalent Argon2id cost " +
            "(3x RunDummyHash) to mask whether the target email is already taken by " +
            "another user. Got unknown={0}ms in-use={1}ms diff={2}ms.",
            medianUnknown, medianAlreadyInUse, diffMs);
    }

    [Fact]
    public async Task Request_for_unchanged_email_has_same_timing_as_unknown_user_branch()
    {
        // Lower-severity channel: an attacker who can submit /email-change/request as
        // the authenticated user could otherwise confirm what the user's current email
        // address is by timing the response. Reauth gate + per-user scope limit blast
        // radius, but the channel was still leaking ≈300ms.
        await using var factory = _factory.WithReplacedService<IEmailService>(new NoopEmailService());

        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"req-unchanged-{Guid.NewGuid():N}@example.com");

        // Warm-up
        var warmupUser = await AuthTestFixture.RegisterUserAsync(_factory, $"warm-unchanged-{Guid.NewGuid():N}@example.com");
        await MeasureRequest(factory, warmupUser, $"warmup-unknown-{Guid.NewGuid():N}@example.com");
        await MeasureRequest(factory, user, user.Email!);

        const int iterations = 7;
        var unknownTimings = new List<long>(iterations);
        var unchangedTimings = new List<long>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            unknownTimings.Add(await MeasureRequest(factory, user, $"never-used-{i}-{Guid.NewGuid():N}@example.com"));
            unchangedTimings.Add(await MeasureRequest(factory, user, user.Email!));
        }

        var medianUnknown = Median(unknownTimings);
        var medianUnchanged = Median(unchangedTimings);
        var diffMs = Math.Abs(medianUnknown - medianUnchanged);

        diffMs.Should().BeLessThan(125,
            "Stage 6.16: EmailUnchanged branch must pay equivalent Argon2id cost " +
            "(3x RunDummyHash) to mask whether the submitted address matches the " +
            "user's current one. Got unknown={0}ms unchanged={1}ms diff={2}ms.",
            medianUnknown, medianUnchanged, diffMs);
    }
}
