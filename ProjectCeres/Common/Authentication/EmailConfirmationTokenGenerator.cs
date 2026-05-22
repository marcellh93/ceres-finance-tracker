using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Generates 256-bit RNG email-confirmation tokens, base64url-encoded, and hashes them
/// via the project Argon2id hasher. Verify reuses the same hasher so timing matches
/// the rest of the auth pipeline.
/// </summary>
public sealed class EmailConfirmationTokenGenerator
{
    private readonly Argon2idPasswordHasher _hasher;

    public EmailConfirmationTokenGenerator(Argon2idPasswordHasher hasher) => _hasher = hasher;

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public string Hash(string token) =>
        _hasher.HashPassword(new ApplicationUser(), token);

    public bool Verify(string token, string storedHash)
    {
        var result = _hasher.VerifyHashedPassword(new ApplicationUser(), storedHash, token);
        return result is PasswordVerificationResult.Success
                       or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
