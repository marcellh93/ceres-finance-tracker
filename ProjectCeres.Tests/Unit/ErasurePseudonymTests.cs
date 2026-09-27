using FluentAssertions;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for the Stage 13.9 erasure pseudonym helper, which pseudonymises a
/// user id for the retained erasure-completion audit-log row via the same
/// HMAC-SHA256 mechanism as <see cref="TokenLookupHasher"/>.
/// </summary>
public class ErasurePseudonymTests
{
    // 32 zero bytes, base64-encoded. The underlying hasher requires >= 32 bytes after decode.
    private const string ValidSecret = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static ErasurePseudonym Build() =>
        new(new TokenLookupHasher(Options.Create(new TokenLookupOptions { Secret = ValidSecret })));

    [Fact]
    public void Compute_is_deterministic_for_the_same_user_id()
    {
        var pseudonym = Build();
        var userId = Guid.NewGuid();

        var a = pseudonym.Compute(userId);
        var b = pseudonym.Compute(userId);

        a.Should().Be(b);
    }

    [Fact]
    public void Compute_differs_between_user_ids()
    {
        var pseudonym = Build();

        var a = pseudonym.Compute(Guid.NewGuid());
        var b = pseudonym.Compute(Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_does_not_return_the_raw_user_id_string()
    {
        var pseudonym = Build();
        var userId = Guid.NewGuid();

        var result = pseudonym.Compute(userId);

        result.Should().NotBe(userId.ToString());
        result.Should().NotBe(userId.ToString("D"));
        result.Should().NotContain(userId.ToString("D"));
    }

    [Fact]
    public void Compute_returns_base64url_with_no_padding()
    {
        var pseudonym = Build();

        var result = pseudonym.Compute(Guid.NewGuid());

        result.Should().NotContain("+");
        result.Should().NotContain("/");
        result.Should().NotContain("=");
    }
}
