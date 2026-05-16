using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using ProjectCeres.Filters;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Filters;

/// <summary>
/// Unit tests for NumberFormatActionFilter.
///
/// The filter is registered globally in Program.cs and runs on every controller
/// action, including [AllowAnonymous] ones. Verifies:
///   1. Anonymous requests skip SettingsService to avoid the RLS 500 crash.
///   2. Authenticated requests still populate ViewData["NumberFormat"].
/// </summary>
public class NumberFormatActionFilterTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// ControllerContext requires a ControllerActionDescriptor, not a plain ActionDescriptor.
    /// </summary>
    private static ControllerActionDescriptor ActionDescriptor() => new();

    private static ClaimsPrincipal Unauthenticated() =>
        new(new ClaimsIdentity()); // no authenticationType → IsAuthenticated == false

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) },
            authenticationType: "Test"));

    private static (ActionContext actionContext, Controller controller) BuildControllerPair(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), ActionDescriptor());
        var controllerMock = new Mock<Controller> { CallBase = false };
        controllerMock.Object.ControllerContext = new ControllerContext(actionContext);
        return (actionContext, controllerMock.Object);
    }

    private static ActionExecutingContext ExecutingContext(ActionContext actionContext, Controller controller) =>
        new(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller);

    // -------------------------------------------------------------------------
    // Test 1 — anonymous request: next() called, GetAsync() never called
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_WhenUserIsAnonymous_SkipsSettingsAndCallsNext()
    {
        // Arrange
        var settingsMock = new Mock<ISettingsService>(MockBehavior.Strict);
        // MockBehavior.Strict: any unexpected call (including GetAsync) will throw.

        var filter = new NumberFormatActionFilter(settingsMock.Object);

        var (actionContext, controller) = BuildControllerPair(Unauthenticated());
        var executingContext = ExecutingContext(actionContext, controller);

        var nextWasCalled = false;
        Task<ActionExecutedContext> Next()
        {
            nextWasCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller));
        }

        // Act
        await filter.OnActionExecutionAsync(executingContext, Next);

        // Assert
        nextWasCalled.Should().BeTrue("the filter must always call next()");
        settingsMock.Verify(s => s.GetAsync(), Times.Never,
            "GetAsync() must not be called for unauthenticated users — it triggers an RLS violation");
    }

    // -------------------------------------------------------------------------
    // Test 2 — authenticated request: GetAsync() called, ViewData set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_WhenUserIsAuthenticated_SetsNumberFormatViewData()
    {
        // Arrange
        const string expectedFormat = "1.234,56";

        var settingsMock = new Mock<ISettingsService>();
        settingsMock
            .Setup(s => s.GetAsync())
            .ReturnsAsync(new Settings { NumberFormat = expectedFormat });

        var filter = new NumberFormatActionFilter(settingsMock.Object);

        var (actionContext, controller) = BuildControllerPair(Authenticated());
        var executingContext = ExecutingContext(actionContext, controller);

        Task<ActionExecutedContext> Next() =>
            Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller));

        // Act
        await filter.OnActionExecutionAsync(executingContext, Next);

        // Assert
        settingsMock.Verify(s => s.GetAsync(), Times.Once,
            "GetAsync() must be called once for authenticated users");
        controller.ViewData["NumberFormat"].Should().Be(expectedFormat,
            "the filter must propagate the user's NumberFormat into ViewData");
    }
}
