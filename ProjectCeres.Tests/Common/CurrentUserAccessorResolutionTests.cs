using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Moq;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.7 / ADR-0073: tests cover the new <see cref="UserContext"/> resolution path
/// (Resolved from cookie, Resolved from scope, PreAuth from <c>[PreAuthCallSite]</c> on
/// the endpoint, Background when an HTTP context exists without auth/tag, Uninitialized
/// when there's no context at all). The legacy <c>UserId</c> convenience accessor is still
/// asserted so existing EF global query filter expressions keep behaving identically.
/// </summary>
public class CurrentUserAccessorResolutionTests
{
    [Fact]
    public void Resolves_from_http_context_first_as_Resolved()
    {
        var claimUser = Guid.NewGuid();
        var http = MockHttp(claim: claimUser);
        var scope = new UserScope();
        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        using (scope.EnterAs(Guid.NewGuid())) // a different id
        {
            sut.Context.Should().BeOfType<UserContext.Resolved>()
                .Which.UserId.Should().Be(claimUser, "HTTP context wins over scope");
            sut.UserId.Should().Be(claimUser);
        }
    }

    [Fact]
    public void Falls_back_to_scope_when_no_http_context_as_Resolved()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var scope = new UserScope();
        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var jobUser = Guid.NewGuid();
        using (scope.EnterAs(jobUser))
        {
            sut.Context.Should().BeOfType<UserContext.Resolved>()
                .Which.UserId.Should().Be(jobUser);
            sut.UserId.Should().Be(jobUser);
        }
    }

    [Fact]
    public void Falls_back_to_scope_when_http_context_has_no_claim_as_Resolved()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);
        var scope = new UserScope();
        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var jobUser = Guid.NewGuid();
        using (scope.EnterAs(jobUser))
        {
            sut.Context.Should().BeOfType<UserContext.Resolved>()
                .Which.UserId.Should().Be(jobUser);
        }
    }

    [Fact]
    public void Endpoint_with_PreAuthCallSite_attribute_resolves_to_PreAuth()
    {
        // No cookie claim, no scope, but the current endpoint declares itself as a
        // pre-auth call site. The accessor reports PreAuth so the interceptor logs
        // Debug instead of Error.
        var attribute = new PreAuthCallSiteAttribute("Auth.Login");
        var ctx = MakeHttpContextWithAttribute(attribute);
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);

        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        sut.Context.Should().BeOfType<UserContext.PreAuth>()
            .Which.CallSite.Should().Be("Auth.Login");
        sut.UserId.Should().Be(Guid.Empty, "PreAuth has no resolved UserId");
    }

    [Fact]
    public void HttpContext_without_auth_or_PreAuthCallSite_resolves_to_Background()
    {
        // HTTP context present but no claim, no scope, no [PreAuthCallSite] attribute.
        // The accessor surfaces Background so the interceptor logs an Error — this is the
        // diagnostic for "an [AllowAnonymous] action that should have been tagged got
        // missed in code review."
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);

        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        sut.Context.Should().BeOfType<UserContext.Background>();
        sut.UserId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void No_http_context_no_scope_resolves_to_Uninitialized()
    {
        // The case that fires at EF model-creation time: global query filter expressions
        // are evaluated eagerly before any HTTP context or scope is established. Must not
        // throw; must surface as Uninitialized so the interceptor stays silent.
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);

        ICurrentUserAccessor sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        sut.Context.Should().BeOfType<UserContext.Uninitialized>();
        sut.UserId.Should().Be(Guid.Empty, "Uninitialized has no resolved UserId");
    }

    private static Mock<IHttpContextAccessor> MockHttp(Guid claim)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, claim.ToString())
            }, "TestScheme"))
        };
        var mock = new Mock<IHttpContextAccessor>();
        mock.SetupGet(h => h.HttpContext).Returns(ctx);
        return mock;
    }

    private static HttpContext MakeHttpContextWithAttribute(PreAuthCallSiteAttribute attribute)
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(attribute),
            "test-endpoint");
        ctx.Features.Set<IEndpointFeature>(new EndpointFeature(endpoint));
        return ctx;
    }

    private sealed class EndpointFeature(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }
}
