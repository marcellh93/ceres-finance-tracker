using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Generates 256-bit RNG erasure-cancel tokens, base64url-encoded, and hashes them via
/// the project Argon2id hasher. Mirror of <see cref="ExportTokenGenerator"/>. Stage 13.9 Task 5.
/// </summary>
public sealed class ErasureTokenGenerator
{
    private readonly Argon2idPasswordHasher _hasher;

    public ErasureTokenGenerator(Argon2idPasswordHasher hasher) => _hasher = hasher;

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
