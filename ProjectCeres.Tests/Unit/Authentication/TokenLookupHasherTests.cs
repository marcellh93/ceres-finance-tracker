using FluentAssertions;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Unit.Authentication;

/// <summary>
/// Unit tests for the Stage 6.15 HMAC-SHA256 token-lookup hasher. The hasher derives
/// an O(1) lookup column from a raw token + server-side secret so /confirm and /revoke
/// endpoints can replace Argon2id candidate-loops with a single indexed query. See
/// docs/superpowers/specs/2026-05-11-stage-6-15-token-lookup-design.md § 5.
/// </summary>
public class TokenLookupHasherTests
{
    // 32 zero bytes, base64-encoded. The hasher requires >= 32 bytes after decode.
    private const string ValidSecret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    // 16 zero bytes, base64-encoded — below the 32-byte minimum.
    private const string ShortSecret = "AAAAAAAAAAAAAAAAAAAAAA==";

    private static TokenLookupHasher Build(string? secret) =>
        new(Options.Create(new TokenLookupOptions { Secret = secret }));

    [Fact]
    public void ComputeLookup_produces_deterministic_32_byte_output()
    {
        var hasher = Build(ValidSecret);
        var a = hasher.ComputeLookup("some-raw-token-value");
        var b = hasher.ComputeLookup("some-raw-token-value");

        a.Should().HaveCount(32);
        b.Should().Equal(a);
    }

    [Fact]
    public void ComputeLookup_differs_for_one_bit_input_change()
    {
        var hasher = Build(ValidSecret);
        var a = hasher.ComputeLookup("some-raw-token-value");
        var b = hasher.ComputeLookup("some-raw-token-valuf"); // last char flipped 1 bit

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Ctor_throws_when_secret_missing()
    {
        var act = () => Build(null);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TokenLookupSecret*");
    }

    [Fact]
    public void Ctor_throws_when_secret_decodes_to_less_than_32_bytes()
    {
        var act = () => Build(ShortSecret);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32*");
    }
}
