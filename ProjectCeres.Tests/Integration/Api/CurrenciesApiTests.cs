using System.Net;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Api;

[Collection("IntegrationParallel2")]
public class CurrenciesApiTests : IntegrationTestBase<Bucket2Factory>
{
    private readonly HttpClient _client;

    public CurrenciesApiTests(Bucket2Factory factory, Bucket2Database bucketDb) : base(factory, bucketDb)
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
