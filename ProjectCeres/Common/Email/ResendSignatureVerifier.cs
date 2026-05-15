using System.Security.Cryptography;
using System.Text;

namespace ProjectCeres.Common.Email;

/// <summary>
/// Verifies the Svix HMAC signature Resend attaches to every webhook delivery. Stage 8e.
///
/// <para>
/// Protocol (Svix): the request carries three headers — <c>Svix-Id</c> (message id),
/// <c>Svix-Timestamp</c> (unix seconds), <c>Svix-Signature</c> (space-separated list of
/// <c>v1,&lt;base64&gt;</c> entries). The HMAC input is
/// <c>"{Svix-Id}.{Svix-Timestamp}.{rawBody}"</c>; the key is the base64-decoded
/// portion of the <c>whsec_…</c> signing secret.
/// </para>
///
/// <para>
/// Two defences beyond the HMAC: a ±5 minute timestamp tolerance to bound replay
/// windows, and <see cref="CryptographicOperations.FixedTimeEquals"/> for the byte
/// comparison so an attacker can't time-side-channel the signature.
/// </para>
/// </summary>
public interface IResendSignatureVerifier
{
    bool Verify(string svixId, string svixTimestamp, string rawBody, string signatureHeader, string secret);
}

public sealed class ResendSignatureVerifier : IResendSignatureVerifier
{
    private const int ToleranceSeconds = 300; // 5 minutes

    public bool Verify(string svixId, string svixTimestamp, string rawBody, string signatureHeader, string secret)
    {
        if (!long.TryParse(svixTimestamp, out var ts)) return false;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > ToleranceSeconds) return false;

        // Strip whsec_ prefix and base64-decode the secret.
        var keyB64 = secret.StartsWith("whsec_") ? secret["whsec_".Length..] : secret;
        byte[] key;
        try { key = Convert.FromBase64String(keyB64.PadRight((keyB64.Length + 3) / 4 * 4, '=')); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(key);
        var toSign = Encoding.UTF8.GetBytes($"{svixId}.{svixTimestamp}.{rawBody}");
        var expected = hmac.ComputeHash(toSign);

        // Signature header is a space-separated list of "v1,<base64>" entries.
        foreach (var entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(',', 2);
            if (parts.Length != 2 || parts[0] != "v1") continue;
            byte[] provided;
            try { provided = Convert.FromBase64String(parts[1]); }
            catch (FormatException) { continue; }
            if (CryptographicOperations.FixedTimeEquals(expected, provided)) return true;
        }
        return false;
    }
}
