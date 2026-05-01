using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationTests")]
public class SettingsApiTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Object);

        body.TryGetProperty("numberFormat", out var numberFormat).Should().BeTrue();
        numberFormat.GetString().Should().BeOneOf("comma_decimal", "period_decimal");

        body.TryGetProperty("dateFormat", out var dateFormat).Should().BeTrue();
        dateFormat.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("defaultCurrencyCode", out var code).Should().BeTrue();
        code.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("defaultCurrencySymbol", out var symbol).Should().BeTrue();
        symbol.GetString().Should().NotBeNullOrEmpty();
    }
}
