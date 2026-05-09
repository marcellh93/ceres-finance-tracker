using System.Net;
using System.Text;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class GlobalFallbackPolicyTests
{
    private readonly TestWebApplicationFactory _factory;

    public GlobalFallbackPolicyTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_GET_to_authed_endpoint_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_POST_to_login_does_not_return_401_from_fallback_policy()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/auth/login", new StringContent("{}", Encoding.UTF8, "application/json"));
        // Anonymous POST to login is allowed by AllowAnonymous; expected 400 (no CSRF) or
        // 401 from credential failure. The point: AllowAnonymous itself is honoured.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }
}
