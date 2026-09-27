using FluentAssertions;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for the Stage 13.9 Task 6b re-registration hold. The hold's
/// EmailFingerprint is computed via the same TokenLookupHasher HMAC mechanism as
/// ErasureRequest.CancelTokenLookup — these tests pin the fingerprint's determinism
/// and the 30-day expiry window the registration-time check relies on.
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

    [Fact]
    public void ExpiresAt_thirty_days_after_ErasedAt_is_still_live_one_second_before_expiry()
    {
        var erasedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hold = new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EmailFingerprint = BuildHasher().ComputeLookup("someone@example.com"),
            ErasedAt = erasedAt,
            ExpiresAt = erasedAt.AddDays(30),
        };

        var justBeforeExpiry = hold.ExpiresAt.AddSeconds(-1);

        (hold.ExpiresAt > justBeforeExpiry).Should().BeTrue(
            "the hold must still block registration one second before its 30-day window closes");
    }

    [Fact]
    public void ExpiresAt_is_not_live_after_the_thirty_day_window_closes()
    {
        var erasedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hold = new ErasedEmailHold
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EmailFingerprint = BuildHasher().ComputeLookup("someone@example.com"),
            ErasedAt = erasedAt,
            ExpiresAt = erasedAt.AddDays(30),
        };

        var justAfterExpiry = hold.ExpiresAt.AddSeconds(1);

        (hold.ExpiresAt > justAfterExpiry).Should().BeFalse(
            "a hold past its 30-day window must no longer block registration");
    }
}
