using System.Net;
using System.Net.Http.Json;
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

// Task 9 — POST /api/profile/erasure + POST /api/profile/erasure/cancel. Erasure is the
// highest-risk surface in this controller: a wrong outcome irreversibly seals or unseals
// an account, so the typed-confirm gate and the cancel-token outcomes are load-bearing.
[Collection("IntegrationParallel3")]
public class ProfileErasureApiTests : IntegrationTestBase<Bucket3AuthFactory>, IAsyncLifetime
{
    private readonly Bucket3AuthFactory _factory;

    public ProfileErasureApiTests(Bucket3AuthFactory factory, Bucket3Database bucketDb) : base(factory, bucketDb) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ErasureRequests
            .IgnoreQueryFilters()
            .Where(r => _seededUserIds.Contains(r.UserId))
            .ExecuteDeleteAsync();
    }

    private readonly List<Guid> _seededUserIds = new();

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string SessionCookie, ApplicationUser User)>
        RegisterAndLoginAsync(string emailPrefix)
    {
        var email = $"profile-erasure-{emailPrefix}-{Guid.NewGuid():N}@example.com";
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

    /// <summary>
    /// Seeds a committed Sealed ErasureRequest (+ sealed user) writing through the admin
    /// context, mirroring ErasureServiceTests.SeedCommittedRequestAsync — the cancel
    /// endpoint is pre-auth and reads via AdminDbContext, so the row must be committed
    /// on a connection separate from any ambient test transaction.
    /// </summary>
    private async Task<string> SeedErasureRequestAsync(Guid userId, ErasureStatus status = ErasureStatus.Sealed)
    {
        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ErasureTokenGenerator>();
        var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();

        var raw = tokens.Generate();
        admin.ErasureRequests.Add(new ErasureRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = status,
            RequestedAt = DateTime.UtcNow,
            ExecuteAfter = DateTime.UtcNow.AddHours(72),
            CancelTokenLookup = lookupHasher.ComputeLookup(raw),
            CancelTokenHash = tokens.Hash(raw),
        });
        await admin.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SealedAt, DateTime.UtcNow));
        await admin.SaveChangesAsync();
        return raw;
    }

    // ── POST /api/profile/erasure ───────────────────────────────────────────────

    [Fact]
    public async Task Post_erasure_without_recent_auth_returns_401_reauth_required()
    {
        var user = await AuthTestFixture.RegisterUserAsync(
            _factory, $"profile-erasure-noreauth-{Guid.NewGuid():N}@example.com");
        _seededUserIds.Add(user.Id);

        var staleCookie = await AuthTestFixture.MintAuthCookieWithLastReauthAt(_factory, user, null);

        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = BuildPost("/api/profile/erasure", staleCookie, user.Id, new { confirm = "ERASE" });

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("REAUTH_REQUIRED");
    }

    [Fact]
    public async Task Post_erasure_with_wrong_confirm_value_returns_422_and_does_not_seal()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("badconfirm");

        var resp = await client.SendAsync(
            BuildPost("/api/profile/erasure", cookie, user.Id, new { confirm = "erase" }));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("\"field\":\"confirm\"");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sealedAt = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == user.Id).Select(u => u.SealedAt).FirstAsync();
        sealedAt.Should().BeNull("a rejected confirm value must not seal the account");
    }

    [Fact]
    public async Task Post_erasure_with_missing_confirm_returns_422()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("noconfirm");

        var resp = await client.SendAsync(BuildPost("/api/profile/erasure", cookie, user.Id));

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Post_erasure_with_valid_confirm_and_recent_auth_returns_202_and_seals_account()
    {
        var (client, cookie, user) = await RegisterAndLoginAsync("success");

        var resp = await client.SendAsync(
            BuildPost("/api/profile/erasure", cookie, user.Id, new { confirm = "ERASE" }));

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Erasure scheduled");

        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var sealedAt = await admin.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == user.Id).Select(u => u.SealedAt).FirstAsync();
        sealedAt.Should().NotBeNull("a successful erasure request must seal the account immediately");
    }

    // ── POST /api/profile/erasure/cancel ────────────────────────────────────────

    [Fact]
    public async Task Post_erasure_cancel_with_valid_token_returns_204_and_unseals_account()
    {
        var (_, _, user) = await RegisterAndLoginAsync("cancel-ok");
        var rawToken = await SeedErasureRequestAsync(user.Id);

        var anonClient = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            _factory, anonClient, "/api/profile/erasure/cancel", new { token = rawToken });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var sealedAt = await admin.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == user.Id).Select(u => u.SealedAt).FirstAsync();
        sealedAt.Should().BeNull("a valid cancel token must unseal the account");
        var status = await admin.ErasureRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.UserId == user.Id).Select(r => r.Status).FirstAsync();
        status.Should().Be(ErasureStatus.Cancelled);
    }

    [Fact]
    public async Task Post_erasure_cancel_with_wrong_token_returns_404()
    {
        var (_, _, user) = await RegisterAndLoginAsync("cancel-wrong");
        await SeedErasureRequestAsync(user.Id);

        var anonClient = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            _factory, anonClient, "/api/profile/erasure/cancel", new { token = "this-is-not-the-token" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_erasure_cancel_on_completed_request_returns_410()
    {
        var (_, _, user) = await RegisterAndLoginAsync("cancel-completed");
        var rawToken = await SeedErasureRequestAsync(user.Id, ErasureStatus.Completed);

        var anonClient = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            _factory, anonClient, "/api/profile/erasure/cancel", new { token = rawToken });

        resp.StatusCode.Should().Be((HttpStatusCode)410);
    }
}
