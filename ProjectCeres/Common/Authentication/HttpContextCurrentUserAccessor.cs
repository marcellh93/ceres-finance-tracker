using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly IUserScope _scope;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor http, IUserScope scope)
    {
        _http = http;
        _scope = scope;
    }

    public Guid UserId
    {
        get
        {
            var claim = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(claim, out var fromCookie)) return fromCookie;

            if (_scope.Current is { } fromScope) return fromScope;

            throw new InvalidOperationException(
                "No user context available. HTTP requests resolve from cookie; " +
                "background jobs must enter via IUserScope.EnterAs().");
        }
    }
}
