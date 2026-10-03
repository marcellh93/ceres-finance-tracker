using System.Net;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Security;

[Collection("IntegrationParallel1")]
public class ForwardedHeadersTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;
    public ForwardedHeadersTests(Bucket1Factory f, Bucket1Database db) : base(f, db)
        => _client = f.CreateClient();

    // With an empty KnownProxies list, a spoofed X-Forwarded-For must NOT be
    // honoured — the endpoint echoes the connection remote IP, which must not
    // become the spoofed value. Asserting 200 OK rules out the vacuous pass
    // where a missing/404 endpoint would also "not contain" the spoofed IP.
    [Fact]
    public async Task Spoofed_x_forwarded_for_does_not_change_remote_ip()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health/remote-ip");
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var ip = await res.Content.ReadAsStringAsync();
        ip.Should().NotContain("203.0.113.7");
    }
}
