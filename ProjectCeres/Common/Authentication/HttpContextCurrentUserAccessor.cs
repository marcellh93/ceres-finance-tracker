using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var claim = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claim, out var id))
            {
                throw new UnauthorizedAccessException("Current user is not authenticated.");
            }
            return id;
        }
    }
}
