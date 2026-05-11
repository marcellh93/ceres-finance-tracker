using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Computes the HMAC-SHA256-derived `TokenLookup` column for `PasswordResetToken`
/// and `EmailChangeToken` so /confirm and /revoke endpoints can locate a row in
/// O(1) instead of running Argon2id against every unconsumed unexpired candidate.
/// See docs/superpowers/specs/2026-05-11-stage-6-15-token-lookup-design.md § 5.
///
/// The secret is bound from `Authentication:TokenLookupSecret:Secret` and stored
/// the same way as `USER_REF_SECRET` (user-secrets locally, hosting secret store
/// in production). Rotation invalidates every active password-reset and
/// email-change token — see security-model.md § Secrets Rotation Procedures.
/// </summary>
public sealed class TokenLookupHasher
{
    private readonly byte[] _key;

    public TokenLookupHasher(IOptions<TokenLookupOptions> options)
    {
        var secret = options.Value.Secret
            ?? throw new InvalidOperationException(
                "Authentication:TokenLookupSecret is required.");

        _key = Convert.FromBase64String(secret);
        if (_key.Length < 32)
        {
            throw new InvalidOperationException(
                "Authentication:TokenLookupSecret must decode to >= 32 bytes.");
        }
    }

    public byte[] ComputeLookup(string rawToken)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
    }
}

public sealed class TokenLookupOptions
{
    public string? Secret { get; set; }
}
