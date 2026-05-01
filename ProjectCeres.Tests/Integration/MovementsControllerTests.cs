using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// WebApplicationFactory tests for Razor-to-SPA migration:
/// — GET /Movements returns 302 redirect to /app/movements
/// </summary>
[Collection("IntegrationTests")]
public class MovementsControllerTests : IAsyncLifetime
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MovementsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // GET /Movements — 302 redirect to SPA shell
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMovementsRoot_Returns302_RedirectingToAppShell()
    {
        using var noRedirectClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await noRedirectClient.GetAsync("/Movements");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be("/app/movements");
    }
}
