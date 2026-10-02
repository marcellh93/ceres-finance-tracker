using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Pseudonymises a user id for the retained erasure-completion audit-log row via
/// the same keyed HMAC-SHA256 as <see cref="TokenLookupHasher"/>. One-way only —
/// never store a reverse lookup table. Stage 13.9 Task 7.
/// </summary>
[RegisterAsSingleton]
public sealed class ErasurePseudonym
{
    private readonly TokenLookupHasher _hasher;

    public ErasurePseudonym(TokenLookupHasher hasher) => _hasher = hasher;

    public string Compute(Guid userId)
    {
        // local pseudonym; migrates to canonical UserRef at Stage 15 (spec D1).
        var bytes = _hasher.ComputeLookup(userId.ToString("D"));
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
