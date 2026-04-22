using FluentAssertions;
using ProjectCeres.Helpers;

namespace ProjectCeres.Tests.Unit;

public class NumberFormatHelperTests
{
    // -------------------------------------------------------------------------
    // FormatAmount — display format, includes thousands separator
    // -------------------------------------------------------------------------

    [Fact]
    public void FormatAmount_CommaDecimal_UsesCommaAsDecimalSeparator()
    {
        NumberFormatHelper.FormatAmount(100.00m, "comma_decimal").Should().Be("100,00");
    }

    [Fact]
    public void FormatAmount_CommaDecimal_UsesThousandsDot()
    {
        NumberFormatHelper.FormatAmount(1234.56m, "comma_decimal").Should().Be("1.234,56");
    }

    [Fact]
    public void FormatAmount_PeriodDecimal_UsesPeriodAsDecimalSeparator()
    {
        NumberFormatHelper.FormatAmount(100.00m, "period_decimal").Should().Be("100.00");
    }

    [Fact]
    public void FormatAmount_PeriodDecimal_UsesThousandsComma()
    {
        NumberFormatHelper.FormatAmount(1234.56m, "period_decimal").Should().Be("1,234.56");
    }

    [Fact]
    public void FormatAmount_NullFormat_FallsBackToInvariant()
    {
        NumberFormatHelper.FormatAmount(1234.56m, null).Should().Be("1,234.56");
    }

    // -------------------------------------------------------------------------
    // FormatInputValue — edit-form format, no thousands separator
    // Critical: output must round-trip correctly through TryParseDecimal
    // -------------------------------------------------------------------------

    [Fact]
    public void FormatInputValue_CommaDecimal_UsesCommaNoThousandsSeparator()
    {
        NumberFormatHelper.FormatInputValue(1234.56m, "comma_decimal").Should().Be("1234,56");
    }

    [Fact]
    public void FormatInputValue_PeriodDecimal_UsesPeriodNoThousandsSeparator()
    {
        NumberFormatHelper.FormatInputValue(1234.56m, "period_decimal").Should().Be("1234.56");
    }

    [Fact]
    public void FormatInputValue_NullFormat_FallsBackToInvariant()
    {
        NumberFormatHelper.FormatInputValue(1234.56m, null).Should().Be("1234.56");
    }

    // -------------------------------------------------------------------------
    // Round-trip: FormatInputValue output must parse back to the original value
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(100.00,  "comma_decimal")]
    [InlineData(1234.56, "comma_decimal")]
    [InlineData(100.00,  "period_decimal")]
    [InlineData(1234.56, "period_decimal")]
    public void FormatInputValue_RoundTrip_ParsesBackToOriginal(double raw, string numberFormat)
    {
        var value     = (decimal)raw;
        var formatted = NumberFormatHelper.FormatInputValue(value, numberFormat);
        NumberFormatHelper.TryParseDecimal(formatted, numberFormat, out var parsed).Should().BeTrue();
        parsed.Should().Be(value);
    }
}
