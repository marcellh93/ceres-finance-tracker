using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Services;
using System.Text;

namespace ProjectCeres.Tests.Unit;

public class HeaderDetectionServiceTests
{
    private static IFormFile CsvFile(string headers)
    {
        var content = headers + "\n2024-01-01,100.00,Test\n";
        var bytes   = Encoding.UTF8.GetBytes(content);
        var stream  = new MemoryStream(bytes);
        var mock    = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns("test.csv");
        mock.Setup(f => f.Length).Returns(stream.Length);
        mock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) => { stream.Position = 0; return stream.CopyToAsync(dest, ct); });
        return mock.Object;
    }

    [Fact]
    public async Task DetectAsync_SpanishBankHeaders_MatchesCorrectly()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("F.Valor,Fecha,Concepto,Movimiento,Importe,Divisa");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().Contain("Fecha");
        result.Headers.Should().Contain("Importe");
        result.DateColumn.Should().Be("Fecha");
        result.AmountColumn.Should().Be("Importe");
        result.DescriptionColumn.Should().Be("Concepto");
        result.CategoryColumn.Should().BeNull(); // no category keyword match
    }

    [Fact]
    public async Task DetectAsync_EnglishHeaders_MatchesCorrectly()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Date,Amount,Description,Category");
        var result = await svc.DetectAsync(file);

        result.DateColumn.Should().Be("Date");
        result.AmountColumn.Should().Be("Amount");
        result.DescriptionColumn.Should().Be("Description");
        result.CategoryColumn.Should().Be("Category");
    }

    [Fact]
    public async Task DetectAsync_NoMatch_ReturnsNullSuggestions()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Col1,Col2,Col3");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().BeEquivalentTo(["Col1", "Col2", "Col3"]);
        result.DateColumn.Should().BeNull();
        result.AmountColumn.Should().BeNull();
        result.DescriptionColumn.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_ReturnsAllHeaders()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("Fecha,Importe,Concepto,Observaciones");
        var result = await svc.DetectAsync(file);

        result.Headers.Should().HaveCount(4);
        result.Headers.Should().Contain("Observaciones");
    }

    [Fact]
    public async Task DetectAsync_OnlyFValorDateColumn_MatchesIt()
    {
        var svc    = new HeaderDetectionService();
        var file   = CsvFile("F.Valor,Importe,Concepto");
        var result = await svc.DetectAsync(file);

        result.DateColumn.Should().Be("F.Valor");
        result.AmountColumn.Should().Be("Importe");
        result.DescriptionColumn.Should().Be("Concepto");
    }
}
