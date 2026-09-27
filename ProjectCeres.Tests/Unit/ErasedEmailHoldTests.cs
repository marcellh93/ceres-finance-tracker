using FluentAssertions;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for the Stage 13.9 Task 6b re-registration hold. The hold's
/// EmailFingerprint is computed via the same TokenLookupHasher HMAC mechanism as
/// ErasureRequest.CancelTokenLookup — these tests pin the fingerprint's determinism.
/// The 30-day expiry boundary itself is exercised at the real call site:
/// AuthController.IsEmailHeldAsync's ExpiresAt > now comparison, via the live/expired
/// integration tests in ErasedEmailHoldRegistrationTests, and the real ErasedAt.AddDays(30)
/// arithmetic in ErasureExecutorTests. Two prior tests here asserted only
/// `DateTime.operator&gt;` on hand-built structs and were removed as tautological —
/// they exercised no ErasedEmailHold or controller behavior.
/// </summary>
public class ErasedEmailHoldTests
{
    // 32 zero bytes, base64-encoded. The underlying hasher requires >= 32 bytes after decode.
    private const string ValidSecret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static TokenLookupHasher BuildHasher() =>
        new(Options.Create(new TokenLookupOptions { Secret = ValidSecret }));

    [Fact]
    public void Fingerprint_is_deterministic_for_the_same_normalized_email()
    {
        var hasher = BuildHasher();

        var a = hasher.ComputeLookup("ERASED-USER@EXAMPLE.COM");
        var b = hasher.ComputeLookup("ERASED-USER@EXAMPLE.COM");

        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Fingerprint_differs_between_emails()
    {
        var hasher = BuildHasher();

        var a = hasher.ComputeLookup("ONE@EXAMPLE.COM");
        var b = hasher.ComputeLookup("TWO@EXAMPLE.COM");

        a.Should().NotBeEquivalentTo(b);
    }
}
