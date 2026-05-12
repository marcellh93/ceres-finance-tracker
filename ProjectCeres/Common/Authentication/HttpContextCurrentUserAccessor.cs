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

            // No HTTP context and no IUserScope.EnterAs() active. Return Guid.Empty so that
            // EF global query filters evaluate to a WHERE clause that matches no rows — the
            // safe default. Callers that legitimately need cross-tenant reads (token lookups,
            // middleware pre-auth, test teardown helpers) must use .IgnoreQueryFilters().
            // The previous throw made this accessor unusable at model-creation time (EF
            // evaluates global filter expressions eagerly on first query).
            return Guid.Empty;
        }
    }
}
