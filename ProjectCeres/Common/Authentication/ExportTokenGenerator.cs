using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Generates 256-bit RNG data-export download tokens, base64url-encoded, and hashes
/// them via the project Argon2id hasher. Mirror of <see cref="EmailChangeTokenGenerator"/>,
/// kept separate for DI/architecture-test clarity. Stage 13.8 Task 6.
/// </summary>
public sealed class ExportTokenGenerator
{
    private readonly Argon2idPasswordHasher _hasher;

    public ExportTokenGenerator(Argon2idPasswordHasher hasher) => _hasher = hasher;

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
