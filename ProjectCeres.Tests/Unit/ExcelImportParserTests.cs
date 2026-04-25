using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class ExcelImportParserTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static IFormFile XlsxFormFile(string fileName)
    {
        var path   = Path.Combine(FixturesDir, fileName);
        var bytes  = File.ReadAllBytes(path);
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

    private static ImportColumnMappings StandardMappings(string? sheetName = null) => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        SheetName         = sheetName
    };

    [Fact]
    public async Task ParseAsync_ValidXlsx_Returns10Rows()
    {
        var file   = XlsxFormFile("valid_import.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings());

        rows.Should().HaveCount(10);
        rows[0].Date.Should().Be(new DateOnly(2024, 1, 1));
        rows[0].Amount.Should().Be(120.00m);
        rows[0].Description.Should().Be("Rent");
    }

    [Fact]
    public async Task ParseAsync_MultiSheet_ReadsFirstSheetByDefault()
    {
        var file   = XlsxFormFile("multi_sheet.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings());

        rows.Should().HaveCount(1);
        rows[0].Description.Should().Be("First sheet row");
    }

    [Fact]
    public async Task ParseAsync_NamedSheet_ReadsCorrectSheet()
    {
        var file   = XlsxFormFile("named_sheet.xlsx");
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, StandardMappings(sheetName: "Transactions"));

        rows.Should().HaveCount(1);
        rows[0].Description.Should().Be("Named sheet row");
        rows[0].Amount.Should().Be(77.00m);
    }

    [Fact]
    public async Task ParseAsync_SpoofedFile_ThrowsInvalidOperationException()
    {
        var spoofedBytes = new byte[] { 0xFF, 0xFE, 0x00, 0x01 }; // not PK magic bytes
        var stream       = new MemoryStream(spoofedBytes);
        var file         = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("fake.xlsx");
        file.Setup(f => f.Length).Returns(spoofedBytes.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });

        var parser = new ExcelImportParser();
        var act    = async () => await parser.ParseAsync(file.Object, StandardMappings());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a valid Excel file*");
    }

    [Fact]
    public void Format_IsExcel()
    {
        new ExcelImportParser().Format.Should().Be(ImportFormat.Excel);
    }
}
