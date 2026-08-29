using FluentAssertions;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Unit.Authentication;

/// <summary>
/// Stage 12.8 — the pending email-change banner shows a masked address so an
/// unattended authenticated screen does not disclose where the account is moving.
/// The owner recognises their own address from the leading characters; a passer-by
/// learns neither the address, its length, nor its domain.
/// </summary>
public class EmailMaskTests
{
    [Theory]
    [InlineData("marcell@gmail.com", "m•••••@g•••••")]
    [InlineData("jo@me.co", "j•••••@m•••••")]
    [InlineData("a@b.io", "a•••••@b•••••")]
    [InlineData("some.very.long.address@subdomain.example.com", "s•••••@s•••••")]
    public void Mask_keeps_one_leading_character_per_part_and_a_fixed_run_of_dots(
        string input, string expected)
    {
        EmailMask.Mask(input).Should().Be(expected);
    }

    [Fact]
    public void Mask_renders_every_address_at_the_same_width_so_length_never_leaks()
    {
        // The whole point of the fixed dot run: a 1-character local part and a
        // 22-character one must be indistinguishable on screen.
        EmailMask.Mask("a@b.io").Length.Should().Be(EmailMask.Mask("some.very.long.address@subdomain.example.com").Length);
    }

    [Fact]
    public void Mask_omits_the_extension_so_it_cannot_narrow_the_domain()
    {
        // .co.uk would say "British"; localhost's absence of an extension would say
        // "internal". Rendering no extension at all removes both signals — and makes
        // the multi-part-extension split (the classic masking bug) unreachable.
        EmailMask.Mask("marcell@gmail.co.uk").Should().Be("m•••••@g•••••");
        EmailMask.Mask("user@localhost").Should().Be("u•••••@l•••••");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_returns_empty_for_absent_input(string? input)
    {
        EmailMask.Mask(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@nolocal.com")]
    [InlineData("nodomain@")]
    public void Mask_never_echoes_input_it_cannot_parse(string input)
    {
        // A malformed address must not fall through to the raw value — that would
        // turn the mask into a disclosure on exactly the inputs nobody validated.
        var masked = EmailMask.Mask(input);
        masked.Should().NotBe(input);
        masked.Should().NotContain("nolocal");
        masked.Should().NotContain("nodomain");
    }
}
