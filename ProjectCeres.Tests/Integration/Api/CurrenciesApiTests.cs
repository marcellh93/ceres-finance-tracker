using System.Net;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationTests")]
public class CurrenciesApiTests
{
    private readonly HttpClient _client;

    public CurrenciesApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetCurrencies_returns_seed_data()
    {
        var response = await _client.GetAsync("/api/currencies");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"code\":\"EUR\"");
        body.Should().Contain("\"code\":\"USD\"");
    }
}
