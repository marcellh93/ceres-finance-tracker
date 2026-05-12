using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Common;

public class CurrentUserAccessorResolutionTests
{
    [Fact]
    public void Resolves_from_http_context_first()
    {
        var claimUser = Guid.NewGuid();
        var http = MockHttp(claim: claimUser);
        var scope = new UserScope();
        var sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        using (scope.EnterAs(Guid.NewGuid())) // a different id
        {
            sut.UserId.Should().Be(claimUser, "HTTP context wins over scope");
        }
    }

    [Fact]
    public void Falls_back_to_scope_when_no_http_context()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var scope = new UserScope();
        var sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var jobUser = Guid.NewGuid();
        using (scope.EnterAs(jobUser))
        {
            sut.UserId.Should().Be(jobUser);
        }
    }

    [Fact]
    public void Falls_back_to_scope_when_http_context_has_no_claim()
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns(ctx);
        var scope = new UserScope();
        var sut = new HttpContextCurrentUserAccessor(http.Object, scope);

        var jobUser = Guid.NewGuid();
        using (scope.EnterAs(jobUser))
        {
            sut.UserId.Should().Be(jobUser);
        }
    }

    [Fact]
    public void Returns_GuidEmpty_when_neither_resolves()
    {
        // Stage 7 Task 9: the accessor cannot throw when neither resolves — EF Core
        // eagerly evaluates global query filter expressions at model creation time,
        // before any HTTP context or IUserScope is established. A throw crashes the
        // app on startup. The safe default is Guid.Empty: query filters then produce
        // a WHERE clause matching zero rows. Cross-tenant callers (token lookups,
        // middleware pre-auth, retention sweeps, test teardown) must opt out via
        // .IgnoreQueryFilters() — the Stage 10 architecture test enforces the
        // boundary so the "no leakage" invariant still holds.
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        sut.UserId.Should().Be(Guid.Empty);
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
}
