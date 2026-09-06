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
    /// <summary>The unconfigured value shipped in appsettings.json. Reaching the hasher means
    /// no real secret was supplied — user-secrets locally, the hosting secret store in Production.</summary>
    internal const string UnconfiguredPlaceholder = "configure-via-user-secrets";

    private const string HowToConfigure =
        "Set it in user-secrets locally (dotnet user-secrets --project ProjectCeres set "
        + "'Authentication:TokenLookupSecret:Secret' \"$(openssl rand -base64 32)\") or in the "
        + "hosting secret store in Production. Tests supply their own fixed value via the factory.";

    private readonly byte[] _key;

    public TokenLookupHasher(IOptions<TokenLookupOptions> options)
    {
        var secret = options.Value.Secret;
        if (string.IsNullOrEmpty(secret) || secret == UnconfiguredPlaceholder)
        {
            // Name the cause + the fix. Without this the placeholder falls straight into
            // Convert.FromBase64String and surfaces as a bare FormatException inside whatever
            // auth path happened to run first — the wrong thing to debug from (Stage 12.12).
            throw new InvalidOperationException(
                "Authentication:TokenLookupSecret:Secret is not configured. " + HowToConfigure);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Authentication:TokenLookupSecret:Secret is not valid base64. " + HowToConfigure, ex);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Authentication:TokenLookupSecret:Secret must decode to >= 32 bytes. " + HowToConfigure);
        }

        _key = key;
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
