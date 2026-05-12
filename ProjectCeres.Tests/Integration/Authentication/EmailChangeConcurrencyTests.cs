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
public class EmailChangeConcurrencyTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeConcurrencyTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Timeout30s() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    private async Task<(WebApplicationFactory<Program> Factory, ApplicationUser User,
                       string OldEmail, string NewEmail, string VerifyToken, string RevokeToken)>
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

        var verifyToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == newEmail));
        var revokeToken = AuthTestFixture.ExtractResetTokenFromMessage(captured.Single(m => m.To == oldEmail));
        captured.Clear();
        return (factory, user, oldEmail, newEmail, verifyToken, revokeToken);
    }

    private static async Task<HttpResponseMessage> PostConfirmAsync(
        WebApplicationFactory<Program> factory, AuthTestWebApplicationFactory baseFactory, string token)
    {
        var (csrf, header) = AuthTestFixture.MintCsrf(baseFactory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/confirm")
        {
            Content = JsonContent.Create(new { token }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return await client.SendAsync(req);
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

    // ── Test #26 ────────────────────────────────────────────────────────────
    // Two concurrent /confirm calls with the same raw token: per-user semaphore
    // + atomic ExecuteUpdateAsync ensures exactly one returns 204; the other 401.
    [Fact]
    public async Task Two_concurrent_confirms_with_same_token_only_one_returns_204_other_401()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var t1 = PostConfirmAsync(factory, _factory, arr.VerifyToken);
        var t2 = PostConfirmAsync(factory, _factory, arr.VerifyToken);
        await Task.WhenAll(t1, t2);

        var statuses = new[] { (await t1).StatusCode, (await t2).StatusCode };
        statuses.Count(s => s == HttpStatusCode.NoContent).Should().Be(1,
            "exactly one concurrent confirm wins");
        statuses.Count(s => s == HttpStatusCode.Unauthorized).Should().Be(1,
            "the loser observes the consumed token and returns 401");
    }

    // ── Test #27 ────────────────────────────────────────────────────────────
    // Two concurrent /request from the same user: only the winner's pair
    // remains active (ConsumedAt == null); the other pair is consumed.
    [Fact]
    public async Task Two_concurrent_requests_for_same_user_one_pair_supersedes_the_other()
    {
        var captured = new List<EmailMessage>();
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(new CapturingEmailService(captured));
            }));

        var oldEmail = $"old-conc-req-{Guid.NewGuid():N}@example.com";
        var newEmailA = $"a-{Guid.NewGuid():N}@example.com";
        var newEmailB = $"b-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, oldEmail);

        var t1 = PostRequestAsync(factory, user, newEmailA);
        var t2 = PostRequestAsync(factory, user, newEmailB);
        await Task.WhenAll(t1, t2);

        (await t1).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await t2).StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id)
            .ToListAsync(Timeout30s());

        rows.Should().HaveCount(4, "two pairs persisted total");
        var active = rows.Where(r => r.ConsumedAt == null).ToList();
        active.Should().HaveCount(2,
            "exactly one pair remains active after supersession");
        active.Select(r => r.NewEmail).Distinct().Should().HaveCount(1,
            "the active pair must share a single NewEmail (the winner's pair)");
    }

    // ── Test #28 ────────────────────────────────────────────────────────────
    // Concurrent /request + /confirm against a partially-consumed state does
    // not corrupt the row pairing. Either /confirm wins (Email updated, both
    // siblings consumed) or /request wins (new pair active, old pair consumed
    // — confirm loses with InvalidToken because its token row got superseded).
    [Fact]
    public async Task Concurrent_request_and_confirm_does_not_corrupt_rows()
    {
        var captured = new List<EmailMessage>();
        var arr = await ArrangePendingChangeAsync(captured);
        await using var factory = arr.Factory;

        var differentNewEmail = $"different-{Guid.NewGuid():N}@example.com";

        var confirmTask = PostConfirmAsync(factory, _factory, arr.VerifyToken);
        var requestTask = PostRequestAsync(factory, arr.User, differentNewEmail);
        await Task.WhenAll(confirmTask, requestTask);

        // Both legal outcomes are acceptable; just assert the DB is internally consistent.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EmailChangeTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == arr.User.Id)
            .ToListAsync(Timeout30s());

        var active = rows.Where(r => r.ConsumedAt == null).ToList();
        // At most one active pair (sibling-paired by NewEmail).
        active.Select(r => r.NewEmail).Distinct().Count().Should().BeLessThanOrEqualTo(1,
            "if any rows are active they must all share one NewEmail (one active pair)");
        // If there's an active pair it has exactly two rows.
        if (active.Count > 0)
        {
            active.Should().HaveCount(2);
            active.Should().Contain(r => r.Purpose == EmailChangeTokenPurpose.VerifyNew);
            active.Should().Contain(r => r.Purpose == EmailChangeTokenPurpose.RevokeOld);
        }
    }
}
