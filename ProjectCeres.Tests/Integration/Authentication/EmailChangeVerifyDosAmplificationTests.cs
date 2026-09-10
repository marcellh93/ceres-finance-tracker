using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 6.15 regression gate: pins the O(1) verify cost of /email-change/confirm and
/// /email-change/revoke against accumulated token load. The 7-day RevokeOld lifetime
/// makes this table the worst pre-6.15 case — N can grow into the thousands under any
/// sustained request rate. Two methods because confirm and revoke filter by different
/// Purpose values and we want independent regression coverage for both.
/// </summary>
[Collection("IntegrationParallel3")]
public class EmailChangeVerifyDosAmplificationTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private const int DummyRowCount = 200;
    private const string MarkerEmailVerify = "dos-amp-emailchange-verify@example.com";
    private const string MarkerEmailRevoke = "dos-amp-emailchange-revoke@example.com";

    private readonly AuthTestWebApplicationFactory _factory;

    public EmailChangeVerifyDosAmplificationTests(AuthTestWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Confirm_with_200_unrelated_VerifyNew_tokens_completes_in_under_1s()
    {
        await SeedDummyTokensAsync(MarkerEmailVerify, EmailChangeTokenPurpose.VerifyNew);
        await AssertConfirmCallReturnsFastAsync(
            url: "/api/auth/email-change/confirm");
    }

    [Fact]
    public async Task Revoke_with_200_unrelated_RevokeOld_tokens_completes_in_under_1s()
    {
        await SeedDummyTokensAsync(MarkerEmailRevoke, EmailChangeTokenPurpose.RevokeOld);
        await AssertConfirmCallReturnsFastAsync(
            url: "/api/auth/email-change/revoke");
    }

    private async Task AssertConfirmCallReturnsFastAsync(string url)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { token = "this-token-is-not-in-any-row" }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrfCookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var sw = Stopwatch.StartNew();
        var resp = await client.SendAsync(req);
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unrelated token must be rejected; the partition the test pins is the wall-clock time, not the outcome");

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            $"Stage 6.15 makes the verify path O(1); current elapsed {sw.ElapsedMilliseconds} ms suggests a candidate-loop regression on {url}");
    }

    private async Task SeedDummyTokensAsync(string markerEmail, EmailChangeTokenPurpose purpose)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = db.Users.FirstOrDefault(u => u.Email == markerEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = markerEmail,
                Email = markerEmail,
                EmailConfirmed = true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // Wipe any prior dummy rows for this marker user so the count stays at exactly N.
        await db.EmailChangeTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ExecuteDeleteAsync();

        var now = DateTime.UtcNow;
        var lifetime = purpose == EmailChangeTokenPurpose.VerifyNew
            ? TimeSpan.FromMinutes(30)
            : TimeSpan.FromDays(7);

        var rows = new List<EmailChangeToken>(DummyRowCount);
        for (var i = 0; i < DummyRowCount; i++)
        {
            rows.Add(new EmailChangeToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Purpose = purpose,
                NewEmail = $"dummy-{i}-{Guid.NewGuid():N}@example.com",
                TokenLookup = RandomNumberGenerator.GetBytes(32),
                TokenHash = "$argon2id$v=19$m=19456,t=2,p=1$" +
                            Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) + "$" +
                            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                CreatedAt = now,
                ExpiresAt = now.Add(lifetime),
                ConsumedAt = null,
            });
        }
        db.EmailChangeTokens.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
