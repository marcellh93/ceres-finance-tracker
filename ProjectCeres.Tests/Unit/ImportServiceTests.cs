using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Unit tests for ImportService — no database required.
/// Fixtures are loaded from the Fixtures/ directory copied to the output.
/// </summary>
public class ImportServiceTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static ImportColumnMappings StandardMappings() => new()
    {
        DateColumn        = "Date",
        AmountColumn      = "Amount",
        DescriptionColumn = "Description",
        CategoryColumn    = "Category"
    };

    private static IFormFile FileFromFixture(string fileName)
    {
        var path   = Path.Combine(FixturesDir, fileName);
        var stream = new MemoryStream(File.ReadAllBytes(path));
        var file   = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.ContentType).Returns(fileName.EndsWith(".xlsx")
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv");
        file.Setup(f => f.OpenReadStream()).Returns(stream);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });
        return file.Object;
    }

    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseAsync_ValidCsv_Returns10RowsWithCorrectFields()
    {
        var service = new ImportService();
        var file    = FileFromFixture("valid_import.csv");
        var mappings = StandardMappings();

        var rows = await service.ParseAsync(file, mappings);

        rows.Should().HaveCount(10);
        rows.First().Date.Should().Be(new DateOnly(2024, 1, 1));
        rows.First().Amount.Should().Be(50.00m);
        rows.First().Description.Should().Be("Grocery store");
    }

    [Fact]
    public async Task ParseAsync_NegativeDebitAmount_IsFlippedToPositive()
    {
        var service  = new ImportService();
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Date",
            AmountColumn      = "Amount",
            DescriptionColumn = "Description",
            FlipDebitSign     = true
        };

        // Build an in-memory CSV with a negative debit value.
        var csv = "Date,Amount,Description\n2024-01-01,-75.50,Supermarket\n";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("debit.csv");
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });

        var rows = await service.ParseAsync(file.Object, mappings);

        rows.Should().HaveCount(1);
        rows.First().Amount.Should().Be(75.50m);
    }

    [Fact]
    public async Task ParseAsync_ColumnMappingApplied_MapsFromCorrectColumns()
    {
        var service  = new ImportService();
        var mappings = new ImportColumnMappings
        {
            DateColumn        = "Txn Date",
            AmountColumn      = "Debit",
            DescriptionColumn = "Memo",
            CategoryColumn    = null
        };

        var csv = "Txn Date,Debit,Memo\n2024-03-15,99.99,ATM withdrawal\n";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns("custom.csv");
        file.Setup(f => f.Length).Returns(stream.Length);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((dest, ct) =>
            {
                stream.Position = 0;
                return stream.CopyToAsync(dest, ct);
            });

        var rows = await service.ParseAsync(file.Object, mappings);

        rows.Should().HaveCount(1);
        rows.First().Date.Should().Be(new DateOnly(2024, 3, 15));
        rows.First().Amount.Should().Be(99.99m);
        rows.First().Description.Should().Be("ATM withdrawal");
    }

    [Fact]
    public async Task ParseAsync_XlsxFile_ReturnsErrorResult()
    {
        var service  = new ImportService();
        var file     = FileFromFixture("xlsx_attempt.xlsx");
        var mappings = StandardMappings();

        var act = async () => await service.ParseAsync(file, mappings);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only CSV files are supported*");
    }

    [Fact]
    public void GenerateFingerprint_SameInputs_ReturnsSameDeterministicHash()
    {
        var service = new ImportService();
        var accountId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var date = new DateOnly(2024, 1, 1);

        var hash1 = service.GenerateFingerprint(date, 50.00m, "Grocery store", accountId);
        var hash2 = service.GenerateFingerprint(date, 50.00m, "Grocery store", accountId);

        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateFingerprint_DifferentAmount_ReturnsDifferentHash()
    {
        var service = new ImportService();
        var accountId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var date = new DateOnly(2024, 1, 1);

        var hash1 = service.GenerateFingerprint(date, 50.00m, "Grocery store", accountId);
        var hash2 = service.GenerateFingerprint(date, 99.99m, "Grocery store", accountId);

        hash1.Should().NotBe(hash2);
    }
}
