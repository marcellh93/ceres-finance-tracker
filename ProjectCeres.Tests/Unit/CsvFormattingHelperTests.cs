using FluentAssertions;
using ProjectCeres.Helpers;

namespace ProjectCeres.Tests.Unit;

public class CsvFormattingHelperTests
{
    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("@user",    "'@user")]
    [InlineData("+1234",    "'+1234")]
    [InlineData("-1234",    "'-1234")]
    public void Csv_DangerousPrefix_IsPrefixedWithSingleQuote(string input, string expected)
        => CsvFormattingHelper.Csv(input).Should().Be(expected);

    [Fact]
    public void Csv_NormalValue_ReturnedUnchanged()
        => CsvFormattingHelper.Csv("Hello world").Should().Be("Hello world");

    [Fact]
    public void Csv_ValueContainingComma_WrappedInDoubleQuotes()
        => CsvFormattingHelper.Csv("Rent, utilities").Should().Be("\"Rent, utilities\"");

    [Fact]
    public void Csv_ValueContainingDoubleQuote_EscapedAndWrapped()
        => CsvFormattingHelper.Csv("Say \"hi\"").Should().Be("\"Say \"\"hi\"\"\"");

    [Fact]
    public void Csv_NullValue_ReturnsEmptyString()
        => CsvFormattingHelper.Csv(null).Should().Be("");

    [Fact]
    public void Csv_EmptyString_ReturnsEmptyString()
        => CsvFormattingHelper.Csv("").Should().Be("");
}
