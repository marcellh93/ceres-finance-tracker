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
    public void Throws_InvalidOperationException_when_neither_resolves()
    {
        var http = new Mock<IHttpContextAccessor>();
        http.SetupGet(h => h.HttpContext).Returns((HttpContext?)null);
        var sut = new HttpContextCurrentUserAccessor(http.Object, new UserScope());

        var act = () => sut.UserId;
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*HTTP requests resolve from cookie*background jobs must enter via IUserScope.EnterAs*");
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
