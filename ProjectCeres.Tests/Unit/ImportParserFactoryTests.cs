using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class ImportParserFactoryTests
{
    private static ImportParserFactory BuildFactory() =>
        new(new CsvImportParser(), new ExcelImportParser());

    [Fact]
    public void GetParser_CsvFormat_ReturnsCsvImportParser()
    {
        var factory = BuildFactory();
        var parser  = factory.GetParser(ImportFormat.Csv);
        parser.Should().BeOfType<CsvImportParser>();
    }

    [Fact]
    public void GetParser_ExcelFormat_ReturnsExcelImportParser()
    {
        var factory = BuildFactory();
        var parser  = factory.GetParser(ImportFormat.Excel);
        parser.Should().BeOfType<ExcelImportParser>();
    }

    [Fact]
    public void GetParser_UnknownFormat_ThrowsArgumentOutOfRangeException()
    {
        var factory = BuildFactory();
        var act     = () => factory.GetParser((ImportFormat)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
