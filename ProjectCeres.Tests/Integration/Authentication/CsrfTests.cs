using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectCeres.Tests.Integration.Authentication;

[Collection("IntegrationTests")]
public class CsrfTests
{
    private readonly TestWebApplicationFactory _factory;

    public CsrfTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task State_changing_request_without_csrf_token_returns_400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // No Cookie header, no X-XSRF-TOKEN header.
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "x@csrf-test.local",
            password = "any-long-password-here-15+",
            rememberMe = false
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "antiforgery filter must reject state-changing requests without X-XSRF-TOKEN");
    }
}
