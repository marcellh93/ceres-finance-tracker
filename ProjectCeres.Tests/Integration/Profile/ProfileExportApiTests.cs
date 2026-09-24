using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration.Profile;

// Task 8 — POST /api/profile/export + GET /api/profile/export/download. The download
// endpoint hands over a full personal-data ZIP, so its negative assertions (404 vs 410,
// dual login+token gate, two-factor lookup+hash verification) are load-bearing.
[Collection("IntegrationParallel3")]
public class ProfileExportApiTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;

    public ProfileExportApiTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ExportJobs
            .IgnoreQueryFilters()
            .Where(j => _seededUserIds.Contains(j.UserId))
            .ExecuteDeleteAsync();
    }

    private readonly List<Guid> _seededUserIds = new();

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string SessionCookie, ApplicationUser User)>
        RegisterAndLoginAsync(string emailPrefix)
    {
        var email = $"profile-export-{emailPrefix}-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);
        _seededUserIds.Add(user.Id);
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var cookie = await AuthTestFixture.LoginViaHttpAsync(_factory, client, email);
        return (client, cookie, user);
    }

    private HttpRequestMessage BuildPost(string url, string sessionCookie, Guid userId, object? body = null)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory, userId);
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={sessionCookie}; {SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return req;
    }

    private HttpRequestMessage BuildGet(string url, string? sessionCookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (sessionCookie is not null)
            req.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={sessionCookie}");
        return req;
    }

    /// <summary>
    /// Seeds a Ready ExportJob for the given user with a known raw token, writing
    /// through IBackgroundJobScope so the RLS WITH CHECK policy accepts the insert
    /// (the row's UserId must match the GUC the write runs under).
    /// </summary>
    private async Task<string> SeedReadyJobAsync(
        Guid userId, DateTime? expiresAt = null, DateTime? consumedAt = null, bool nullStoredPath = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ExportTokenGenerator>();
        var jobScope = scope.ServiceProvider.GetRequiredService<IBackgroundJobScope>();

        var rawToken = tokenGenerator.Generate();
        var tempPath = Path.Combine(Path.GetTempPath(), $"ceres-export-test-{Guid.NewGuid():N}.zip");
        await File.WriteAllBytesAsync(tempPath, [0x50, 0x4b, 0x03, 0x04]); // ZIP local-file-header magic

        await jobScope.RunAsync(userId, nameof(SeedReadyJobAsync), async () =>
        {
            db.ExportJobs.Add(new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Status = ExportJobStatus.Ready,
                Format = ExportFormat.Zip,
                RequestedAt = DateTime.UtcNow.AddMinutes(-5),
                ReadyAt = DateTime.UtcNow.AddMinutes(-1),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(24),
                ConsumedAt = consumedAt,
                StoredPath = nullStoredPath ? null : tempPath,
                TokenLookup = lookupHasher.ComputeLookup(rawToken),
                TokenHash = tokenGenerator.Hash(rawToken),
            });
            await db.SaveChangesAsync();
        });

        return rawToken;
    }

    // ── POST /api/profile/export ────────────────────────────────────────────────

    [Fact]
    public async Task Post_export_without_recent_auth_returns_401_reauth_required()
    {
        var user = await AuthTestFixture.RegisterUserAsync(
            _factory, $"profile-export-noreauth-{Guid.NewGuid():N}@example.com");
        _seededUserIds.Add(user.Id);

        var staleCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sid = await db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Id)
            .FirstAsync();

        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = BuildPost("/api/profile/export", staleCookie, user.Id, new { format = "zip" });

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");

        // sid is fetched only to prove the session row backing the stale cookie exists —
        // guards against a false-401 from an unrelated session lookup failure.
        sid.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Post_export_success_returns_202_with_jobId_and_message()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("success");

        var resp = await client.SendAsync(BuildPost("/api/profile/export", cookie, user.Id));
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        data.GetProperty("jobId").GetGuid().Should().NotBe(Guid.Empty);
        data.GetProperty("message").GetString().Should()
            .Be("Export started. You will be notified by email when ready.");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.ExportJobs.IgnoreQueryFilters()
            .SingleAsync(j => j.UserId == user.Id);
        persisted.Status.Should().Be(ExportJobStatus.Pending);
    }

    [Fact]
    public async Task Post_export_with_pending_job_returns_same_jobId_dedupe()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("dedupe");

        var first = await client.SendAsync(BuildPost("/api/profile/export", cookie, user.Id));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstJobId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("jobId").GetGuid();

        // Rate limit is 1/24h — the SAME underlying dedupe call is exercised through
        // the service directly for the second attempt, matching what the endpoint
        // would return absent the limiter. This proves dedupe, not the limiter, which
        // Post_export_twice_within_24h_returns_429 covers separately.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobScope = scope.ServiceProvider.GetRequiredService<IBackgroundJobScope>();
        ExportJob? second = null;
        await jobScope.RunAsync(user.Id, nameof(Post_export_with_pending_job_returns_same_jobId_dedupe), async () =>
        {
            var svc = scope.ServiceProvider.GetRequiredService<IExportJobService>();
            second = await svc.CreateOrGetPendingAsync(CancellationToken.None);
        });

        second!.Id.Should().Be(firstJobId, "an existing Pending job must be reused, never duplicated");
        var count = await db.ExportJobs.IgnoreQueryFilters().CountAsync(j => j.UserId == user.Id);
        count.Should().Be(1, "no second row should have been inserted");
    }

    [Fact]
    public async Task Post_export_with_unsupported_format_returns_422()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("badformat");

        var resp = await client.SendAsync(
            BuildPost("/api/profile/export", cookie, user.Id, new { format = "json" }));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var any = await db.ExportJobs.IgnoreQueryFilters().AnyAsync(j => j.UserId == user.Id);
        any.Should().BeFalse("a rejected format must not create a job");
    }

    [Fact]
    public async Task Post_export_twice_within_24h_returns_429_with_retry_after()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("ratelimit");

        var first = await client.SendAsync(BuildPost("/api/profile/export", cookie, user.Id));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await client.SendAsync(BuildPost("/api/profile/export", cookie, user.Id));
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.Headers.RetryAfter.Should().NotBeNull();
    }

    // ── GET /api/profile/export/download ────────────────────────────────────────

    [Fact]
    public async Task Get_download_with_valid_token_logged_in_and_ready_job_returns_200_zip_bytes()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("download-ok");
        var rawToken = await SeedReadyJobAsync(user.Id);

        var resp = await client.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}", cookie));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith([0x50, 0x4b, 0x03, 0x04]);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.ExportJobs.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.UserId == user.Id);
        job.ConsumedAt.Should().NotBeNull("a successful download must stamp ConsumedAt (single-use)");
    }

    [Fact]
    public async Task Get_download_with_valid_token_but_not_logged_in_returns_401()
    {
        var (_, _, user) = await RegisterAndLoginAsync("download-noauth");
        var rawToken = await SeedReadyJobAsync(user.Id);

        var anonClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var resp = await anonClient.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}", sessionCookie: null));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the token alone must not authenticate — login AND token are both required (D6 dual gate)");
    }

    [Fact]
    public async Task Get_download_with_consumed_token_returns_410()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("download-consumed");
        var rawToken = await SeedReadyJobAsync(user.Id, consumedAt: DateTime.UtcNow.AddMinutes(-1));

        var resp = await client.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}", cookie));

        resp.StatusCode.Should().Be((HttpStatusCode)410);
    }

    [Fact]
    public async Task Get_download_with_expired_job_returns_410()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("download-expired");
        var rawToken = await SeedReadyJobAsync(user.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var resp = await client.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}", cookie));

        resp.StatusCode.Should().Be((HttpStatusCode)410);
    }

    [Fact]
    public async Task Get_download_with_another_users_token_returns_404_not_403()
    {
        var (clientA, cookieA, _) = await RegisterAndLoginAsync("download-idor-a");
        var (_, _, userB) = await RegisterAndLoginAsync("download-idor-b");
        var rawTokenB = await SeedReadyJobAsync(userB.Id);

        var resp = await clientA.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawTokenB)}", cookieA));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "RLS makes user B's job invisible to user A — this must be 404, never 403 (no existence leak)");
    }

    [Fact]
    public async Task Get_download_where_lookup_matches_but_hash_does_not_verify_returns_404()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("download-forged-hash");
        var rawToken = await SeedReadyJobAsync(user.Id);

        // Overwrite the stored hash with a REAL Argon2id hash of a DIFFERENT raw value
        // after seeding: TokenLookup still matches (finds the row via the original
        // token's lookup fingerprint), but verifying the original raw token against
        // this new, syntactically-valid-but-different hash must fail closed. This is
        // the two-factor guard — a lookup collision or forged token cannot pass without
        // the second (Argon2id) factor.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenGenerator = scope.ServiceProvider.GetRequiredService<ExportTokenGenerator>();
            var jobScope = scope.ServiceProvider.GetRequiredService<IBackgroundJobScope>();
            var wrongHash = tokenGenerator.Hash(tokenGenerator.Generate());
            await jobScope.RunAsync(user.Id, nameof(Get_download_where_lookup_matches_but_hash_does_not_verify_returns_404), async () =>
            {
                var job = await db.ExportJobs.SingleAsync(j => j.UserId == user.Id);
                job.TokenHash = wrongHash;
                await db.SaveChangesAsync();
            });
        }

        var resp = await client.SendAsync(
            BuildGet($"/api/profile/export/download?token={Uri.EscapeDataString(rawToken)}", cookie));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "TokenLookup narrowed to a row, but the Argon2id factor must still fail closed to 404");
    }
}
