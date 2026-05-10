using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class PersistentTokenService
{
    private readonly Argon2idPasswordHasher _hasher;

    public PersistentTokenService(Argon2idPasswordHasher hasher) => _hasher = hasher;

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
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <summary>
    /// Format the persistent cookie as `{base64url(sessionIdBytes)}.{secret}`. Lookup
    /// becomes O(1) by indexed UserSession.Id instead of a linear Argon2id scan over
    /// every active session row (Stage 6b.3 Gap 3).
    /// </summary>
    public string FormatCookie(Guid sessionId, string secret)
    {
        var idBytes = sessionId.ToByteArray();
        var idEncoded = Convert.ToBase64String(idBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{idEncoded}.{secret}";
    }

    /// <summary>
    /// Parse a persistent cookie value into (sessionId, secret). Returns null on any
    /// malformed input — separator missing, base64url invalid, byte length wrong.
    /// </summary>
    public (Guid sessionId, string secret)? TryParseCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return null;
        var dot = cookie.IndexOf('.');
        if (dot <= 0 || dot >= cookie.Length - 1) return null;

        var idEncoded = cookie[..dot];
        var secret = cookie[(dot + 1)..];

        // Reverse base64url -> base64.
        var padded = idEncoded.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 0: break;
            default: return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 16) return null;
            return (new Guid(bytes), secret);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
