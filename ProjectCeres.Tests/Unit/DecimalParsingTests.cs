using FluentAssertions;
using ProjectCeres.Helpers;

namespace ProjectCeres.Tests.Unit;

public class DecimalParsingTests
{
    // -------------------------------------------------------------------------
    // comma_decimal mode (es-ES: period = thousands sep, comma = decimal sep)
    // -------------------------------------------------------------------------

    [Fact]
    public void CommaDecimal_NativeFormat_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("100,00", "comma_decimal", out var result).Should().BeTrue();
        result.Should().Be(100.00m);
    }

    [Fact]
    public void CommaDecimal_InvariantFormat_ParsesCorrectlyInsteadOfCorrupting()
    {
        // The bug: "100.00" in comma_decimal would parse as 10000 without the fix.
        NumberFormatHelper.TryParseDecimal("100.00", "comma_decimal", out var result).Should().BeTrue();
        result.Should().Be(100.00m);
    }

    [Fact]
    public void CommaDecimal_NativeThousandsSeparator_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("1.234,56", "comma_decimal", out var result).Should().BeTrue();
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void CommaDecimal_InvariantThousandsSeparator_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("1,234.56", "comma_decimal", out var result).Should().BeTrue();
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void CommaDecimal_WholeNumber_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("500", "comma_decimal", out var result).Should().BeTrue();
        result.Should().Be(500m);
    }

    // -------------------------------------------------------------------------
    // period_decimal mode (invariant: comma = thousands sep, period = decimal sep)
    // -------------------------------------------------------------------------

    [Fact]
    public void PeriodDecimal_NativeFormat_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("100.00", "period_decimal", out var result).Should().BeTrue();
        result.Should().Be(100.00m);
    }

    [Fact]
    public void PeriodDecimal_NativeThousandsSeparator_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("1,234.56", "period_decimal", out var result).Should().BeTrue();
        result.Should().Be(1234.56m);
    }

    [Fact]
    public void PeriodDecimal_WholeNumber_ParsesCorrectly()
    {
        NumberFormatHelper.TryParseDecimal("500", "period_decimal", out var result).Should().BeTrue();
        result.Should().Be(500m);
    }

    // -------------------------------------------------------------------------
    // Invalid input
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("abc",   "comma_decimal")]
    [InlineData("abc",   "period_decimal")]
    [InlineData("12x.5", "comma_decimal")]
    [InlineData("--100", "period_decimal")]
    public void InvalidInput_ReturnsFalse(string raw, string numberFormat)
    {
        NumberFormatHelper.TryParseDecimal(raw, numberFormat, out _).Should().BeFalse();
    }
}
