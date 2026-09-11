using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Stage 6.15 defence-in-depth coverage. The refactored verify path locates a
/// candidate row by indexed <c>TokenLookup</c>, then runs ONE Argon2id verify
/// against the row's <c>TokenHash</c>. The Argon2id check is the second factor:
/// if a row has a matching lookup but a mismatched hash, it means either an
/// HMAC-SHA256 collision (cryptographically infeasible) or direct database
/// tampering. Either way the verify endpoint must return its standard
/// <c>INVALID_*_TOKEN</c> rejection, not silently accept.
///
/// These tests seed a row directly with a deliberately-mismatched <c>TokenHash</c>
/// (so the lookup hits but the Argon2id verify fails) and assert the controller
/// returns 401. Without these tests, a future refactor that deletes the Argon2id
/// check would still pass the existing happy-path/expiry/consumed suite.
/// </summary>
[Collection("IntegrationParallel2")]
public class TokenLookupTamperResistanceTests : IntegrationTestBase<AuthTestWebApplicationFactory>, IClassFixture<AuthTestWebApplicationFactory>
{
    private readonly AuthTestWebApplicationFactory _factory;

    public TokenLookupTamperResistanceTests(AuthTestWebApplicationFactory factory, Bucket2Database bucketDb) : base(factory, bucketDb) => 
        _factory = factory;

    [Fact]
    public async Task PasswordReset_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
    {
        var (rawToken, _) = await SeedPasswordResetTamperedRowAsync();

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(
            _factory, client, "/api/auth/password-reset/confirm",
            new { token = rawToken, newPassword = "fresh horse battery staple" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the matched row's TokenHash does not Argon2id-verify against the raw token — defence-in-depth check must reject");
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task EmailChange_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
    {
        var rawToken = await SeedEmailChangeTamperedRowAsync(EmailChangeTokenPurpose.VerifyNew);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/confirm")
        {
            Content = JsonContent.Create(new { token = rawToken }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the matched VerifyNew row's TokenHash does not Argon2id-verify against the raw token — defence-in-depth check must reject");
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    [Fact]
    public async Task EmailChange_revoke_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
    {
        var rawToken = await SeedEmailChangeTamperedRowAsync(EmailChangeTokenPurpose.RevokeOld);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var (csrf, header) = AuthTestFixture.MintCsrf(_factory);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/email-change/revoke")
        {
            Content = JsonContent.Create(new { token = rawToken }),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={csrf}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the matched RevokeOld row's TokenHash does not Argon2id-verify against the raw token — defence-in-depth check must reject");
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_EMAIL_CHANGE_TOKEN");
    }

    [Fact]
    public async Task LockoutUnlock_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401()
    {
        // Mirror EmailChange_confirm_with_matching_TokenLookup_but_wrong_TokenHash_returns_401.
        // Seed a LockoutUnlockToken row where TokenLookup matches a real raw token but
        // TokenHash is for a DIFFERENT raw token. POSTing the real raw token must:
        //   (1) hit the indexed TokenLookup query (the row's TokenLookup matches),
        //   (2) fail the Argon2id verify (the row's TokenHash does not match the raw),
        //   (3) return 401 with the LockoutUnlock invalid-token envelope.
        // If a future refactor deletes the Argon2id check, this test goes red while the
        // happy-path/expiry/consumed suite stays green — that's the defence-in-depth gap
        // it's here to catch.
        string realToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();
            var generator = scope.ServiceProvider.GetRequiredService<LockoutUnlockTokenGenerator>();

            realToken = generator.Generate();
            var forgedToken = generator.Generate();

            var email = $"tamper-lockout-{Guid.NewGuid():N}@tamper-test.local";
            var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

            db.LockoutUnlockTokens.Add(new LockoutUnlockToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenLookup = lookupHasher.ComputeLookup(realToken),
                TokenHash = generator.Hash(forgedToken),  // mismatched on purpose
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + LockoutUnlockService.TokenLifetime,
                ConsumedAt = null,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var resp = await AuthTestFixture.PostJsonWithCsrfAsync(_factory, client,
            "/api/auth/lockout-unlock", new { token = realToken });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the matched row's TokenHash does not Argon2id-verify against the raw token — defence-in-depth check must reject");
        (await resp.Content.ReadAsStringAsync()).Should().Contain("INVALID_LOCKOUT_UNLOCK_TOKEN");
    }

    /// <summary>Returns a raw token whose TokenLookup matches a seeded row, but whose
    /// TokenHash was generated from an UNRELATED raw token so Argon2id verify fails.</summary>
    private async Task<(string RawToken, Guid UserId)> SeedPasswordResetTamperedRowAsync()
    {
        var email = $"tamper-pwreset-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<PasswordResetTokenGenerator>();
        var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();

        var realToken = generator.Generate();
        var decoyToken = generator.Generate(); // distinct value — Argon2id verify must fail
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenLookup = lookupHasher.ComputeLookup(realToken),
            TokenHash = generator.Hash(decoyToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            ConsumedAt = null,
            MfaVerifiedAt = null,
        });
        await db.SaveChangesAsync();
        return (realToken, user.Id);
    }

    private async Task<string> SeedEmailChangeTamperedRowAsync(EmailChangeTokenPurpose purpose)
    {
        var email = $"tamper-emailchange-{purpose}-{Guid.NewGuid():N}@example.com";
        var user = await AuthTestFixture.RegisterUserAsync(_factory, email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generator = scope.ServiceProvider.GetRequiredService<EmailChangeTokenGenerator>();
        var lookupHasher = scope.ServiceProvider.GetRequiredService<TokenLookupHasher>();

        var realToken = generator.Generate();
        var decoyToken = generator.Generate();
        var lifetime = purpose == EmailChangeTokenPurpose.VerifyNew
            ? TimeSpan.FromMinutes(30)
            : TimeSpan.FromDays(7);

        db.EmailChangeTokens.Add(new EmailChangeToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = purpose,
            NewEmail = $"new-{Guid.NewGuid():N}@example.com",
            TokenLookup = lookupHasher.ComputeLookup(realToken),
            TokenHash = generator.Hash(decoyToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            ConsumedAt = null,
        });
        await db.SaveChangesAsync();
        return realToken;
    }
}
