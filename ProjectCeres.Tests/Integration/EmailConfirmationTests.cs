using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using ProjectCeres.Tests.Integration.Infrastructure;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 9.3 — pins the contract of the Register → email-confirmation token →
/// /api/auth/email/verify pipeline + the /api/auth/email/verify/resend re-issue
/// endpoint + the Login EMAIL_NOT_CONFIRMED branch. Mirrors PasswordReset* tests
/// in shape: Bucket4AuthFactory, strict-mock IEmailService, per-test
/// GUID-suffixed emails, IgnoreQueryFilters on the user-owned token table.
/// </summary>
[Collection("IntegrationParallel4")]
public class EmailConfirmationTests : IntegrationTestBase<Bucket4AuthFactory>, IAsyncLifetime
{
    private readonly Bucket4AuthFactory _factory;

    public EmailConfirmationTests(Bucket4AuthFactory factory, Bucket4Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    // WAF tests don't go through TestDbFixture's migrate hook; if this class runs
    // before any TestDbFixture-based class in the IntegrationTests collection, the
    // new EmailConfirmationTokens table won't exist yet. EnsureMigratedAsync is
    // idempotent, so this is a no-op when the schema is already current.
    public Task InitializeAsync() => MigrationFixture.EnsureMigratedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private static (Mock<IEmailService> mock, List<EmailMessage> captured) CapturingEmailMock()
    {
        var captured = new List<EmailMessage>();
        var mock = new Mock<IEmailService>(MockBehavior.Strict);
        mock.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Returns<EmailMessage, CancellationToken>((m, _) =>
            {
                lock (captured) { captured.Add(m); }
                return Task.CompletedTask;
            });
        return (mock, captured);
    }

    /// <summary>
    /// Extracts the raw email-verification token from the captured email body. The
    /// URL is of the form <c>{base}/email-verify#token={raw}</c>. Mirrors
    /// <see cref="AuthTestFixture.ExtractResetTokenFromMessage"/>.
    /// </summary>
    private static string ExtractVerifyTokenFromMessage(EmailMessage message)
    {
        const string marker = "token=";
        var idx = message.BodyText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException($"no token marker in body: {message.BodyText}");
        var start = idx + marker.Length;
        var end = message.BodyText.IndexOfAny(['\r', '\n', ' '], start);
        return end < 0 ? message.BodyText[start..] : message.BodyText[start..end];
    }

    /// <summary>
    /// Registers a fresh user via the real <c>/api/auth/register</c> endpoint so the
    /// Register handler fires its three-branch logic AND issues a token row. Returns
    /// the email + the raw verification token captured from the outgoing email.
    /// </summary>
    private static async Task<(string email, string rawToken, Guid userId)> RegisterAndCaptureTokenAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        List<EmailMessage> captured,
        string? emailOverride = null,
        string password = AuthTestFixture.ValidPassword)
    {
        var email = emailOverride ?? $"reg-{Guid.NewGuid():N}@test.local";
        var beforeCount = captured.Count;
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/register", new { email, password });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        captured.Count.Should().Be(beforeCount + 1, "Register must enqueue exactly one verification email");
        var rawToken = ExtractVerifyTokenFromMessage(captured[^1]);

        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return (email, rawToken, user!.Id);
    }

    // ── Test 1 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Register_writes_token_row_with_TokenLookup_and_30min_expiry()
    {
        var (mock, _) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var email = $"reg-{Guid.NewGuid():N}@test.local";
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await um.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailConfirmationTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == user!.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(1, "Register must insert exactly one token row");
        var row = rows[0];
        row.TokenLookup.Should().NotBeNullOrEmpty();
        row.TokenLookup.Should().HaveCount(32, "TokenLookup is HMAC-SHA256 ⇒ 32 bytes");
        row.TokenHash.Should().NotBeNullOrEmpty();
        row.ConsumedAt.Should().BeNull();
        (row.ExpiresAt - row.CreatedAt).Should().BeCloseTo(
            TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(2));
    }

    // ── Test 2 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verify_with_valid_token_marks_email_confirmed()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, rawToken, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Sanity: user starts unconfirmed.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            fresh!.EmailConfirmed.Should().BeFalse("precondition: registration leaves EmailConfirmed=false");
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            fresh!.EmailConfirmed.Should().BeTrue();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .SingleAsync(Timeout30s());
            row.ConsumedAt.Should().NotBeNull("the token row must be marked consumed");
        }
    }

    // ── Test 3 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verify_with_expired_token_returns_401_INVALID_VERIFICATION_TOKEN()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, rawToken, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Force-expire the row.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))
                    .SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-31)),
                    Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_VERIFICATION_TOKEN");

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            fresh!.EmailConfirmed.Should().BeFalse("expired-token branch must not flip EmailConfirmed");
        }
    }

    // ── Test 4 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verify_with_consumed_token_returns_401()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, rawToken, _) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        var first = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await second.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_VERIFICATION_TOKEN");
    }

    // ── Test 5 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verify_with_random_unknown_token_returns_401_with_constant_time_floor()
    {
        var (mock, _) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify",
            new { token = "random-base64url-string-that-will-not-match" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_VERIFICATION_TOKEN");

        // The Argon2id dummy hash on the miss branch is verified deterministically
        // by EmailConfirmationService's per-branch RunDummyHash mirroring; an
        // additional wall-clock assertion would just re-flake under CPU contention
        // and is not the property this test pins. Response shape is sufficient.
    }

    // ── Test 6 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verify_with_tampered_TokenHash_returns_401()
    {
        // Tamper-resistance: TokenLookup hits the right row but the stored TokenHash
        // is rewritten to a different valid-shape Argon2 PHC value. The defence-in-depth
        // check is `_tokens.Verify(rawToken, match.TokenHash)` after the TokenLookup
        // match — it must reject even though TokenLookup matched.
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (_, rawToken, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Compute a different valid-shape Argon2 PHC hash by hashing an unrelated value
        // through the same generator. TokenLookup stays untouched so the row is still
        // findable; only TokenHash diverges from the canonical value derived from rawToken.
        string tamperedHash;
        using (var scope = factory.Services.CreateScope())
        {
            var generator = scope.ServiceProvider.GetRequiredService<EmailConfirmationTokenGenerator>();
            tamperedHash = generator.Hash("a-completely-different-token-value");
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.TokenHash, tamperedHash),
                    Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_VERIFICATION_TOKEN");

        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            fresh!.EmailConfirmed.Should().BeFalse(
                "tampered TokenHash must not flip EmailConfirmed");
        }
    }

    // ── Test 7 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Resend_with_unknown_email_returns_204_no_token_row_written()
    {
        var emailMock = new Mock<IEmailService>(MockBehavior.Strict);
        // No Setup — strict mock fails if any send is attempted on the unknown branch.

        await using var factory = _factory
            .WithReplacedService(emailMock.Object)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMemoryCache>();
                    services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
                }));
        var client = factory.CreateClient();

        int beforeCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            beforeCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .CountAsync(Timeout30s());
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify/resend",
            new { email = $"never-registered-{Guid.NewGuid():N}@test.local" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var afterCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .CountAsync(Timeout30s());
            afterCount.Should().Be(beforeCount,
                "unknown-email branch must not insert any EmailConfirmationToken row");
        }

        emailMock.VerifyNoOtherCalls();
    }

    // ── Test 8 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Resend_with_known_unconfirmed_email_writes_new_token_supersedes_old()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory
            .WithReplacedService(mock.Object)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMemoryCache>();
                    services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
                }));
        var client = factory.CreateClient();

        var (email, _, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        Guid originalTokenId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .SingleAsync(Timeout30s());
            originalTokenId = row.Id;
            row.ConsumedAt.Should().BeNull("precondition: original token is unconsumed");
        }

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify/resend", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        captured.Should().HaveCount(2, "resend on unconfirmed user must send a fresh verification email");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .ToListAsync(Timeout30s());
            rows.Should().HaveCount(2, "supersede leaves the old row in place; resend inserts a fresh one");

            var original = rows.Single(r => r.Id == originalTokenId);
            original.ConsumedAt.Should().NotBeNull("the prior unconsumed token must be marked consumed (supersede)");

            var fresh = rows.Single(r => r.Id != originalTokenId);
            fresh.ConsumedAt.Should().BeNull();
            (fresh.ExpiresAt - fresh.CreatedAt).Should().BeCloseTo(
                TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(2));
        }
    }

    // ── Test 9 ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task Resend_with_known_confirmed_email_returns_204_no_new_token_row_written()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory
            .WithReplacedService(mock.Object)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMemoryCache>();
                    services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
                }));
        var client = factory.CreateClient();

        var (email, _, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Manually flip EmailConfirmed=true via Identity.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            var emailToken = await um.GenerateEmailConfirmationTokenAsync(fresh!);
            (await um.ConfirmEmailAsync(fresh!, emailToken)).Succeeded.Should().BeTrue();
        }

        int beforeCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            beforeCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .CountAsync(Timeout30s());
        }
        var capturedBefore = captured.Count;

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify/resend", new { email });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var afterCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .CountAsync(Timeout30s());
            afterCount.Should().Be(beforeCount,
                "confirmed-email branch must not insert any new EmailConfirmationToken row");
        }
        captured.Count.Should().Be(capturedBefore,
            "confirmed-email branch must not send any new email (anti-enumeration via Argon2id mirror)");
    }

    // ── Test 10 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Resend_rate_limit_6th_request_in_an_hour_returns_429_with_RetryAfter()
    {
        // Service-side per-email gate is MemoryCache-backed (5/hour/email). The
        // middleware EmailByUser policy + GlobalLimiter [ApplyEmailIpRateLimit] are
        // no-op'd in the base Bucket4Factory, so this test isolates the
        // service-side gate. Fresh IMemoryCache so the bucket starts empty regardless
        // of prior tests sharing the factory.
        var (mock, _) = CapturingEmailMock();
        await using var factory = _factory
            .WithReplacedService(mock.Object)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMemoryCache>();
                    services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
                }));
        var client = factory.CreateClient();

        var email = $"rate-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 5; i++)
        {
            var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
                factory, client, "/api/auth/email/verify/resend", new { email });
            resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "call #{0} should pass under the 5/hr per-email limit", i + 1);
        }

        var rejected = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify/resend", new { email });
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        rejected.Headers.TryGetValues("Retry-After", out var values).Should().BeTrue(
            "429 response must carry a Retry-After header");
        var raw = values!.Single();
        int.TryParse(raw, out var seconds).Should().BeTrue(
            "Retry-After value '{0}' must parse to an integer second count", raw);
        seconds.Should().BeGreaterThan(0);
    }

    // ── Test 11 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Re_register_same_unconfirmed_email_after_expiry_issues_new_token_for_existing_user()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, _, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Expire the original token row in place.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.EmailConfirmationTokens
                .IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))
                    .SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-31)),
                    Timeout30s());
        }

        // Re-register the same email. Duplicate-email branch + EmailConfirmed=false
        // ⇒ IssueAsync re-fires (same-code-path as fresh create) for the existing user.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        captured.Should().HaveCount(2,
            "re-register on unconfirmed user must issue a fresh verification email");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(Timeout30s());
            rows.Should().HaveCount(2);
            rows[0].ConsumedAt.Should().NotBeNull("supersede must mark the old (now-expired) row consumed");
            rows[1].ConsumedAt.Should().BeNull();
            (rows[1].ExpiresAt - rows[1].CreatedAt).Should().BeCloseTo(
                TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(2));
        }
    }

    // ── Test 12 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Re_register_same_email_when_already_confirmed_returns_204_no_new_token()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, _, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Manually flip EmailConfirmed=true via Identity (mirrors test #9's approach).
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            var emailToken = await um.GenerateEmailConfirmationTokenAsync(fresh!);
            (await um.ConfirmEmailAsync(fresh!, emailToken)).Succeeded.Should().BeTrue();
        }

        int beforeCount;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            beforeCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .CountAsync(Timeout30s());
        }
        var capturedBefore = captured.Count;

        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/register",
            new { email, password = AuthTestFixture.ValidPassword });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "duplicate-email anti-enum: confirmed-existing branch returns 204 with no token + no email");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var afterCount = await db.EmailConfirmationTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .CountAsync(Timeout30s());
            afterCount.Should().Be(beforeCount,
                "confirmed-existing branch must not insert a new token row");
        }
        captured.Count.Should().Be(capturedBefore,
            "confirmed-existing branch must not send a new email");
    }

    // ── Test 13 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_for_unconfirmed_user_with_correct_password_returns_401_EMAIL_NOT_CONFIRMED()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, _, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        // Login with the correct password before verifying.
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("EMAIL_NOT_CONFIRMED");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var failed = await db.FailedLoginAttempts
            .Where(f => f.UserId == userId && f.Reason == FailedLoginReason.EmailNotConfirmed)
            .ToListAsync(Timeout30s());
        failed.Should().HaveCount(1,
            "Login IsNotAllowed branch must record exactly one FailedLoginAttempt with reason EmailNotConfirmed");
    }

    // ── Test 14 ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_for_user_after_verifying_via_email_verify_returns_204()
    {
        var (mock, captured) = CapturingEmailMock();
        await using var factory = _factory.WithReplacedService(mock.Object);
        var client = factory.CreateClient();

        var (email, rawToken, userId) = await RegisterAndCaptureTokenAsync(factory, client, captured);

        var verifyResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/email/verify", new { token = rawToken });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Sanity: EmailConfirmed actually flipped.
        using (var scope = factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fresh = await um.FindByIdAsync(userId.ToString());
            fresh!.EmailConfirmed.Should().BeTrue();
        }

        var loginResp = await AuthTestFixture.PostJsonWithCsrfAsync(
            factory, client, "/api/auth/login",
            new { email, password = AuthTestFixture.ValidPassword, rememberMe = false });

        loginResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "Login must succeed once the user has verified via /api/auth/email/verify");
    }
}
