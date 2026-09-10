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

[Collection("IntegrationParallel2")]
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
        captured.Should().Contain(m => m.To.Address == newEmail && m.Subject.Contains("Confirm"));
        captured.Should().Contain(m => m.To.Address == oldEmail && m.Subject.Contains("Email change requested"));

        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To.Address == newEmail));
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To.Address == oldEmail));
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

    // ── Stage 6.16 timing-channel regressions (count-based, 2026-05-12) ─────
    // Pin that the EmailAlreadyInUse and EmailUnchanged fast-return branches perform
    // the same number of Argon2id operations as the user-is-null/Accepted reference
    // branch. Pre-6.16 the fast-returns did zero Argon2id work; the fix added three
    // RunDummyHash calls to each. Deterministic count assertion replaces the prior
    // wall-clock measurement which was flake-prone under integration-suite load.

    [Fact]
    public async Task Request_for_email_already_in_use_performs_same_Argon2id_count_as_unknown_user_branch()
    {
        // Highest-severity timing channel pre-6.16: an authenticated user could enumerate
        // OTHER users' email addresses because the EmailAlreadyInUse fast-return path
        // skipped all Argon2id work that the happy path performed.
        await using var factory = _factory.WithReplacedServiceAndArgon2idCounter<IEmailService>(
            new NoopEmailService(), out var counter);

        var requester = await AuthTestFixture.RegisterUserAsync(_factory, $"req-already-{Guid.NewGuid():N}@example.com");
        var occupier  = await AuthTestFixture.RegisterUserAsync(_factory, $"occ-{Guid.NewGuid():N}@example.com");

        // Warm up JIT for the counting hasher path.
        await PostRequestAsync(factory, requester, $"warmup-{Guid.NewGuid():N}@example.com");

        // Unknown-user branch (the test reaches it by submitting an email belonging
        // to no one; the requester is authenticated, but the new-email target is not
        // a registered user, so the existing-user check at line 95 passes through to
        // the happy path. To hit the user-is-null fast-return on line 82–86 we need
        // an authenticated request whose userId resolves to no row — which requires
        // a deleted user. Simpler: compare AlreadyInUse against the never-used branch
        // because both share the SAME reference: the happy-path Argon2id count.
        // The user-null branch isn't reachable from a normal authenticated request.)
        //
        // Reference: never-used email address → happy path → 3 Argon2id (one
        // equalisation hash + two _tokens.Hash for the Verify + Revoke tokens).
        counter.Reset();
        var referenceResp = await PostRequestAsync(factory, requester, $"never-used-{Guid.NewGuid():N}@example.com");
        referenceResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var referenceCount = counter.Count;

        // EmailAlreadyInUse branch: requester submits the occupier's email.
        counter.Reset();
        var inUseResp = await PostRequestAsync(factory, requester, occupier.Email!);
        inUseResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var inUseCount = counter.Count;

        inUseCount.Should().Be(referenceCount,
            "EmailAlreadyInUse fast-return must perform the same number of Argon2id " +
            "operations as the happy path. reference={0}, in-use={1}. Pre-Stage-6.16 " +
            "this leaked cross-user email enumeration because the fast-return did " +
            "zero Argon2id work; the fix adds three RunDummyHash calls to mirror the " +
            "happy path's equalisation hash + two token-hashes.",
            referenceCount, inUseCount);

        referenceCount.Should().BeGreaterThan(0,
            "the happy path must perform at least one Argon2id (RunDummyHash + token-hashes)");
    }

    [Fact]
    public async Task Request_for_unchanged_email_performs_same_Argon2id_count_as_unknown_branch()
    {
        // Lower-severity timing channel pre-6.16: an attacker who could submit
        // /email-change/request as the authenticated user could otherwise confirm
        // the user's current email by timing the response.
        await using var factory = _factory.WithReplacedServiceAndArgon2idCounter<IEmailService>(
            new NoopEmailService(), out var counter);

        var user = await AuthTestFixture.RegisterUserAsync(_factory, $"req-unchanged-{Guid.NewGuid():N}@example.com");

        // Warm up JIT.
        await PostRequestAsync(factory, user, $"warmup-{Guid.NewGuid():N}@example.com");

        // Reference: never-used email → happy path → 3 Argon2id (equalisation +
        // Verify + Revoke token hashes).
        counter.Reset();
        var referenceResp = await PostRequestAsync(factory, user, $"never-used-{Guid.NewGuid():N}@example.com");
        referenceResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var referenceCount = counter.Count;

        // EmailUnchanged branch: user submits their own current email.
        counter.Reset();
        var unchangedResp = await PostRequestAsync(factory, user, user.Email!);
        unchangedResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var unchangedCount = counter.Count;

        unchangedCount.Should().Be(referenceCount,
            "EmailUnchanged fast-return must perform the same number of Argon2id " +
            "operations as the happy path. reference={0}, unchanged={1}. Pre-Stage-6.16 " +
            "this leaked the user's current-email-address confirmation via response " +
            "timing.",
            referenceCount, unchangedCount);

        referenceCount.Should().BeGreaterThan(0,
            "the happy path must perform at least one Argon2id");
    }
}
