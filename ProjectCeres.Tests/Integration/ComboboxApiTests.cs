using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationParallel1")]
public class ComboboxApiTests(Bucket1Factory factory, Bucket1Database bucketDb)
    : IntegrationTestBase<Bucket1Factory>(factory, bucketDb)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetActiveAccounts_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/accounts/active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("currencyCode", out _).Should().BeTrue();
            item.TryGetProperty("currencySymbol", out _).Should().BeTrue();
            item.TryGetProperty("accountTypeName", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetActiveCategories_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/categories/active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var item in body.EnumerateArray())
        {
            item.TryGetProperty("id", out _).Should().BeTrue();
            item.TryGetProperty("name", out _).Should().BeTrue();
            item.TryGetProperty("categoryTypeName", out _).Should().BeTrue();
        }
    }
}
