using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectCeres.Tests.Integration;

public class ApiInfrastructureTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetHealth_Returns200_WithStatusOk()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task PostToApiEndpoint_WithInvalidBody_Returns422_WithValidationErrorShape()
    {
        var payload = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/health/validate", payload);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        error.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        error.TryGetProperty("details", out _).Should().BeTrue();
    }
}
