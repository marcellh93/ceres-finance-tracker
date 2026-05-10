using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Shared helpers for Stage 6a auth integration tests. RegisterUserAsync bypasses
/// the email-confirmation flow per spec § 8 transitional rule (production flow
/// is register → email-confirm → login; in 6a there is no email service).
/// </summary>
public static class AuthTestFixture
{
    public const string ValidPassword = "correct horse battery staple";

    public static async Task<ApplicationUser> RegisterUserAsync(
        AuthTestWebApplicationFactory factory, string email, string password = ValidPassword)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue("expected user to be created: {0}",
            string.Join(", ", result.Errors.Select(e => e.Description)));

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmed = await userManager.ConfirmEmailAsync(user, token);
        confirmed.Succeeded.Should().BeTrue();
        return user;
    }

    /// <summary>
    /// Overload of <see cref="RegisterUserAsync(AuthTestWebApplicationFactory,string,string)"/>
    /// that accepts a base <see cref="WebApplicationFactory{Program}"/> — used when the caller
    /// has a derived factory from <c>WithReplacedService</c> or <c>WithCapturedLogger</c>.
    /// </summary>
    public static async Task<ApplicationUser> RegisterUserAsync(
        WebApplicationFactory<Program> factory, string email, string password = ValidPassword)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue("expected user to be created: {0}",
            string.Join(", ", result.Errors.Select(e => e.Description)));

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmed = await userManager.ConfirmEmailAsync(user, token);
        confirmed.Succeeded.Should().BeTrue();
        return user;
    }

    /// <summary>
    /// Overload of <see cref="MintCsrf(AuthTestWebApplicationFactory,Guid?)"/> that accepts
    /// a base <see cref="WebApplicationFactory{Program}"/>.
    /// </summary>
    public static (string CookieValue, string HeaderValue) MintCsrf(
        WebApplicationFactory<Program> factory, Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (userId is { } id)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Name, id.ToString()),
                },
                authenticationType: "Test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return (tokens.CookieToken!, tokens.RequestToken!);
    }

    /// <summary>
    /// Overload of <see cref="PostJsonWithCsrfAsync{T}(AuthTestWebApplicationFactory,HttpClient,string,T)"/>
    /// that accepts a base <see cref="WebApplicationFactory{Program}"/> for minting the CSRF token.
    /// </summary>
    public static Task<HttpResponseMessage> PostJsonWithCsrfAsync<T>(
        WebApplicationFactory<Program> factory, HttpClient client, string url, T body)
    {
        var (cookie, header) = MintCsrf(factory);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return client.SendAsync(req);
    }

    /// <summary>
    /// Mints an antiforgery token pair from the running factory. If userId is non-null,
    /// the synthetic HttpContext used for minting carries that user's NameIdentifier
    /// claim, so the resulting token validates against authenticated requests for that
    /// user. If userId is null, the pair is anonymous-bound.
    /// </summary>
    public static (string CookieValue, string HeaderValue) MintCsrf(
        AuthTestWebApplicationFactory factory, Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var antiforgery = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (userId is { } id)
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Name, id.ToString()),
                },
                authenticationType: "Test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        return (tokens.CookieToken!, tokens.RequestToken!);
    }

    /// <summary>
    /// POSTs JSON with a valid anonymous CSRF token attached. For the first POST
    /// in a test where the user is not yet authenticated (login, register).
    /// </summary>
    public static Task<HttpResponseMessage> PostJsonWithCsrfAsync<T>(
        AuthTestWebApplicationFactory factory, HttpClient client, string url, T body)
    {
        var (cookie, header) = MintCsrf(factory);
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Cookie", $"{SessionConstants.CsrfCookieName}={cookie}");
        req.Headers.Add(SessionConstants.CsrfHeaderName, header);
        return client.SendAsync(req);
    }

    /// <summary>
    /// Enrolls a user's TOTP via Identity's built-in flow. Generates an authenticator
    /// key, computes a current TOTP code, and flips TwoFactorEnabled to true. Returns
    /// the seed (base32 string) so subsequent test code can compute fresh codes for
    /// login attempts.
    /// </summary>
    public static async Task<string> EnrollUserMfaAsync(
        AuthTestWebApplicationFactory factory, ApplicationUser user)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var freshUser = await userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException("user vanished between Register + Enroll");

        await userManager.ResetAuthenticatorKeyAsync(freshUser);
        var seed = await userManager.GetAuthenticatorKeyAsync(freshUser)
            ?? throw new InvalidOperationException("authenticator key not set after generate");

        var code = ComputeCurrentTotpCode(seed);
        var verified = await userManager.VerifyTwoFactorTokenAsync(
            freshUser, TokenOptions.DefaultAuthenticatorProvider, code);
        verified.Should().BeTrue("freshly-generated code must verify");

        await userManager.SetTwoFactorEnabledAsync(freshUser, true);
        return seed;
    }

    /// <summary>
    /// Computes the current 6-digit TOTP code for a base32-encoded seed using the
    /// standard RFC 6238 algorithm with 30-second period and SHA1. Mirrors what
    /// Identity does internally; we replicate it here so tests can produce codes
    /// without standing up an authenticator app.
    /// </summary>
    public static string ComputeCurrentTotpCode(string base32Seed)
    {
        var key = DecodeBase32(base32Seed);
        var counter = (long)Math.Floor(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30.0);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new System.Security.Cryptography.HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Extracts the raw reset token from an <see cref="EmailMessage"/> body. The body
    /// is expected to contain a segment of the form <c>token=&lt;value&gt;</c> where
    /// the value ends at the next whitespace or end-of-string. Throws if no marker is
    /// found, so tests fail fast if the email format changes.
    /// </summary>
    public static string ExtractResetTokenFromMessage(EmailMessage message)
    {
        var marker = "token=";
        var idx = message.BodyText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException($"no token marker in body: {message.BodyText}");
        var start = idx + marker.Length;
        var end = message.BodyText.IndexOfAny(new[] { '\r', '\n', ' ' }, start);
        return end < 0 ? message.BodyText[start..] : message.BodyText[start..end];
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.ToUpperInvariant())
        {
            if (c == '=') break;
            var idx = alphabet.IndexOf(c);
            if (idx < 0) continue;
            buffer = (buffer << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return output.ToArray();
    }
}
