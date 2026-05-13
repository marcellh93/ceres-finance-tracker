using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Moq;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Common;

public class PreAuthCallSiteTaggerTests
{
    [Theory]
    [InlineData("Auth", "Register")]
    [InlineData("Auth", "Login")]
    [InlineData("Auth", "LoginTotp")]
    [InlineData("Auth", "PasswordResetRequest")]
    [InlineData("LockoutUnlock", "Confirm")]
    public void Returns_true_for_each_documented_preauth_route(string controller, string action)
    {
        var http = MockHttpWithEndpoint(controller, action);
        var sut = new PreAuthCallSiteTagger(http);

        sut.IsLegitimatePreAuth().Should().BeTrue();
    }

    [Theory]
    [InlineData("Auth", "Logout")]
    [InlineData("Transactions", "Index")]
    [InlineData("Settings", "Update")]
    [InlineData("Dashboard", "Index")]
    public void Returns_false_for_unknown_controller_action_pairs(string controller, string action)
    {
        var http = MockHttpWithEndpoint(controller, action);
        var sut = new PreAuthCallSiteTagger(http);

        sut.IsLegitimatePreAuth().Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_HttpContext_is_null_background_thread_case()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var sut = new PreAuthCallSiteTagger(http.Object);

        sut.IsLegitimatePreAuth().Should().BeFalse();
    }

    [Fact]
    public void Returns_false_when_endpoint_has_no_controller_action_descriptor()
    {
        // Hits the path where GetEndpoint() returns an Endpoint without controller
        // metadata (e.g. static files, health checks).
        var ctx = new DefaultHttpContext();
        var endpoint = new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "static");
        ctx.Features.Set<IEndpointFeature>(new EndpointFeature(endpoint));
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);

        var sut = new PreAuthCallSiteTagger(http.Object);

        sut.IsLegitimatePreAuth().Should().BeFalse();
    }

    private static IHttpContextAccessor MockHttpWithEndpoint(string controller, string action)
    {
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = controller,
            ActionName = action,
        };
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(descriptor),
            $"{controller}.{action}");
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IEndpointFeature>(new EndpointFeature(endpoint));

        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);
        return http.Object;
    }

    private sealed class EndpointFeature(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }
}
