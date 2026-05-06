using System.Globalization;
using ClosedXML.Excel;
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

    [Fact]
    public async Task ParseAsync_NumericAmountAndTypedDateCells_ParsesIndependentlyOfThreadCulture()
    {
        // Reproduces the comma-decimal corruption: when the file stores Importe as a
        // numeric cell and the host runs under es-ES, GetString() returns "120,54",
        // which an invariant decimal.TryParse silently reads as 12054. Reading the
        // cell through its typed accessor sidesteps the string round-trip entirely.
        var file   = XlsxNumericAmountAndTypedDate();
        var parser = new ExcelImportParser();

        var rows = await RunUnderCultureAsync(new CultureInfo("es-ES"),
            () => parser.ParseAsync(file, new ImportColumnMappings
            {
                DateColumn        = "Fecha",
                AmountColumn      = "Importe",
                DescriptionColumn = "Concepto"
            }));

        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be(new DateOnly(2026, 5, 5));
        rows[0].Amount.Should().Be(120.54m);
        rows[1].Date.Should().Be(new DateOnly(2026, 5, 3));
        rows[1].Amount.Should().Be(-8.79m);
    }

    [Fact]
    public async Task ParseAsync_CommaDecimalTextAmount_ParsesCorrectly()
    {
        // Some banks export amounts as text with a comma decimal separator.
        // Invariant parsing rejects "120,54" (or worse, treats the comma as a thousands
        // group separator) — the parser must fall back to a comma-decimal culture.
        var file   = XlsxCommaDecimalTextAmount();
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, new ImportColumnMappings
        {
            DateColumn        = "Fecha",
            AmountColumn      = "Importe",
            DescriptionColumn = "Concepto"
        });

        rows.Should().HaveCount(1);
        rows[0].Amount.Should().Be(120.54m);
    }

    private static IFormFile XlsxNumericAmountAndTypedDate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell("A1").Value = "Fecha";
        ws.Cell("B1").Value = "Importe";
        ws.Cell("C1").Value = "Concepto";

        ws.Cell("A2").Value = new DateTime(2026, 5, 5);
        ws.Cell("B2").Value = 120.54;
        ws.Cell("C2").Value = "Recibo mes anterior";

        ws.Cell("A3").Value = new DateTime(2026, 5, 3);
        ws.Cell("B3").Value = -8.79;
        ws.Cell("C3").Value = "Crunchyroll.com";

        return SaveAsFormFile(wb, "numeric.xlsx");
    }

    private static IFormFile XlsxCommaDecimalTextAmount()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell("A1").Value = "Fecha";
        ws.Cell("B1").Value = "Importe";
        ws.Cell("C1").Value = "Concepto";

        ws.Cell("A2").Value = "05/05/2026";
        // Force text storage so GetString() returns the literal "120,54"
        // regardless of thread culture or ClosedXML's number-formatting heuristics.
        var amountCell = ws.Cell("B2");
        amountCell.Style.NumberFormat.Format = "@";
        amountCell.Value = "120,54";
        ws.Cell("C2").Value = "Recibo mes anterior";

        return SaveAsFormFile(wb, "comma.xlsx");
    }

    private static IFormFile SaveAsFormFile(XLWorkbook wb, string fileName)
    {
        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var file = new Mock<IFormFile>();
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

    private static async Task<T> RunUnderCultureAsync<T>(CultureInfo culture, Func<Task<T>> action)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try { return await action(); }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public async Task ParseAsync_BankBannerAboveHeaders_FindsHeadersAndParsesRows()
    {
        // Mimics real-world bank exports (e.g. BBVA) where rows 1-4 contain a logo banner,
        // report title, and metadata, and the actual table headers sit on row 5.
        var file   = XlsxWithBannerAboveHeaders();
        var parser = new ExcelImportParser();

        var rows = await parser.ParseAsync(file, new ImportColumnMappings
        {
            DateColumn        = "Fecha",
            AmountColumn      = "Importe",
            DescriptionColumn = "Concepto"
        });

        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be(new DateOnly(2026, 5, 5));
        rows[0].Amount.Should().Be(120.54m);
        rows[0].Description.Should().Be("Recibo mes anterior");
        rows[1].Date.Should().Be(new DateOnly(2026, 5, 3));
        rows[1].Amount.Should().Be(-8.79m);
        rows[1].Description.Should().Be("Crunchyroll.com");
    }

    private static IFormFile XlsxWithBannerAboveHeaders()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");

        ws.Cell("F2").Value = "Listado de movimiento";
        ws.Cell("F3").Value = "Fecha de generación del informe: 06/05/2026";

        ws.Cell("B5").Value = "Fecha";
        ws.Cell("C5").Value = "Tarjeta";
        ws.Cell("D5").Value = "Concepto";
        ws.Cell("E5").Value = "Importe";
        ws.Cell("F5").Value = "Divisa";

        ws.Cell("B6").Value = "05/05/2026";
        ws.Cell("C6").Value = "4552232399534570";
        ws.Cell("D6").Value = "Recibo mes anterior";
        ws.Cell("E6").Value = "120.54";
        ws.Cell("F6").Value = "€";

        ws.Cell("B7").Value = "03/05/2026";
        ws.Cell("C7").Value = "4552232399534570";
        ws.Cell("D7").Value = "Crunchyroll.com";
        ws.Cell("E7").Value = "-8.79";
        ws.Cell("F7").Value = "€";

        return SaveAsFormFile(wb, "bbva.xlsx");
    }
}
