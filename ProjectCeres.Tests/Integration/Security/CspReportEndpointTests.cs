using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Security;

[Collection("IntegrationParallel1")]
public class CspReportEndpointTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;

    public CspReportEndpointTests(Bucket1Factory factory, Bucket1Database bucketDb) : base(factory, bucketDb)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Anonymous_post_is_accepted_with_204()
    {
        var body = new { cspReport = new { violatedDirective = "script-src" } };
        var res = await _client.PostAsJsonAsync("/api/csp-report", body);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
