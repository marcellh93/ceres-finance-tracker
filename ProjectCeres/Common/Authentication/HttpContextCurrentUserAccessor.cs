using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor http, IUserScope scope) : ICurrentUserAccessor
{
    public Guid UserId
    {
        get
        {
            var claim = http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(claim, out var fromCookie)) return fromCookie;

            if (scope.Current is { } fromScope) return fromScope;

            throw new InvalidOperationException(
                "No user context available. HTTP requests resolve from cookie; " +
                "background jobs must enter via IUserScope.EnterAs().");
        }
    }
}
