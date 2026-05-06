using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class CsvImportParserTests
{
    private static IFormFile CsvFormFile(string content, string fileName = "test.csv")
    {
        var bytes  = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var file   = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });
        return file.Object;
    }

    [Fact]
    public async Task ParseAsync_ValidCsv_ReturnsParsedRows()
    {
        var csv = "Date,Amount,Description\n2024-01-01,100.00,Rent\n2024-01-02,50.00,Groceries";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description"
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be(new DateOnly(2024, 1, 1));
        rows[0].Amount.Should().Be(100.00m);
        rows[0].Description.Should().Be("Rent");
    }

    [Fact]
    public async Task ParseAsync_NegativeDebit_PreservesSignWithFlipDebitSignTrue()
    {
        // FlipDebitSign is a no-op: direction is inferred downstream from the raw
        // signed amount. Pre-fix this row was returned as +75 and then misclassified
        // as income — see spec 2026-04-29-import-column-mapping-debit-credit-design.md.
        var csv  = "Date,Amount,Description\n2024-01-01,-75.00,Electricity";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description",
            FlipDebitSign     = true
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows[0].Amount.Should().Be(-75.00m);
    }

    [Fact]
    public void Format_IsCsv()
    {
        new CsvImportParser().Format.Should().Be(ImportFormat.Csv);
    }

    [Fact]
    public async Task ParseAsync_CommaDecimalAmount_ParsesCorrectly()
    {
        // Spanish bank CSV exports quote comma-decimal amounts so the comma isn't
        // mistaken for a delimiter. The parser must recognise "120,54" as 120.54,
        // not silently inflate it to 12054 by treating the comma as a thousands sep.
        var csv = "Date,Amount,Description\n2024-01-01,\"120,54\",Rent";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description"
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows.Should().HaveCount(1);
        rows[0].Amount.Should().Be(120.54m);
    }

    [Fact]
    public async Task ParseAsync_PeriodDecimalStillWorks_AfterCommaFix()
    {
        // Regression guard: tightening the invariant NumberStyles to reject group
        // separators must not break the common period-decimal case.
        var csv  = "Date,Amount,Description\n2024-01-01,120.54,Rent";
        var file = CsvFormFile(csv);
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description"
        };

        var parser = new CsvImportParser();
        var rows   = await parser.ParseAsync(file, mappings);

        rows[0].Amount.Should().Be(120.54m);
    }
}
