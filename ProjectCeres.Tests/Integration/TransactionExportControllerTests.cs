using System.Net;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class TransactionExportControllerTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetExport_NoFilter_Returns200WithCsvContentType()
    {
        var response = await _client.GetAsync("/Transactions/Export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }

    [Fact]
    public async Task GetExport_WithDateRange_Returns200WithCsvContentType()
    {
        var response = await _client.GetAsync("/Transactions/Export?from=2025-01-01&to=2025-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
    }
}
