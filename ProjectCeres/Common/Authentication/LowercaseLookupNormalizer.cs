using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Stage 9.1.5.b §4.6: ILookupNormalizer replacement that returns lowercase
/// normalized emails/keys instead of Identity's default uppercase
/// (UpperInvariantLookupNormalizer). Preserves the prior
/// FailedLoginRecorder.TruncateAndNormalize semantics (lowercase + trim) while
/// consolidating the normalization site onto Identity's interface so
/// LockoutCache, FailedLoginRecorder, and UserManager.NormalizeEmail all
/// produce identical keys.
/// </summary>
public sealed class LowercaseLookupNormalizer : ILookupNormalizer
{
    public string? Normalize(string? key) => key?.ToLowerInvariant();
    public string? NormalizeName(string? name) => name?.ToLowerInvariant();
    public string? NormalizeEmail(string? email) => email?.ToLowerInvariant();
}
