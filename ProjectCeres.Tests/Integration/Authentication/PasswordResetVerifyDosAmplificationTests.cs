using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 6.15 regression gate: pins the O(1) verify cost of /password-reset/confirm
/// against accumulated token load. Pre-6.15 the candidate loop ran one Argon2id verify
/// per unconsumed unexpired row (20-40s at N=200 under integration-test load); post-6.15
/// the indexed TokenLookup lookup makes the verify cost independent of N. The test
/// seeds 200 dummy rows and asserts the confirm call still returns in under one second.
/// </summary>
[Collection("IntegrationParallel2")]
public class PasswordResetVerifyDosAmplificationTests : IClassFixture<AuthTestWebApplicationFactory>
{
    private const int DummyRowCount = 200;
    private const string MarkerEmail = "dos-amp-pwreset@example.com";

    private readonly AuthTestWebApplicationFactory _factory;

    public PasswordResetVerifyDosAmplificationTests(AuthTestWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Confirm_with_200_unrelated_tokens_completes_in_under_1s()
    {
        await SeedDummyTokensAsync();

        var client = _factory.CreateClient();

        var sw = Stopwatch.StartNew();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            _factory, client, "/api/auth/password-reset/confirm",
            new { token = "this-token-is-not-in-any-row", newPassword = "fresh horse battery staple" });
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an unrelated token must be rejected; the partition the test pins is the wall-clock time, not the outcome");

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            $"Stage 6.15 makes the verify path O(1); current elapsed {sw.ElapsedMilliseconds} ms suggests a candidate-loop regression");
    }

    private async Task SeedDummyTokensAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Marker user so the dummy rows are scoped to one identifiable user; cleanup
        // happens at the AuthTestWebApplicationFactory level via test-DB lifecycle.
        var user = db.Users.FirstOrDefault(u => u.Email == MarkerEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = MarkerEmail,
                Email = MarkerEmail,
                EmailConfirmed = true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // Wipe any prior dummy rows for this marker user so the count stays at exactly N.
        await db.PasswordResetTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ExecuteDeleteAsync();

        var now = DateTime.UtcNow;
        var rows = new List<PasswordResetToken>(DummyRowCount);
        for (var i = 0; i < DummyRowCount; i++)
        {
            rows.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenLookup = RandomNumberGenerator.GetBytes(32),
                // Dummy Argon2id-shaped hash; never verified by the confirm call because
                // the indexed lookup misses on the unrelated test token.
                TokenHash = "$argon2id$v=19$m=19456,t=2,p=1$" +
                            Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) + "$" +
                            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(15),
                ConsumedAt = null,
                MfaVerifiedAt = null,
            });
        }
        db.PasswordResetTokens.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
