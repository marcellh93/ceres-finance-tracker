using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace ProjectCeres.Common;

public sealed class PreAuthCallSiteTagger(IHttpContextAccessor http) : IPreAuthCallSiteTagger
{
    private static readonly HashSet<string> PreAuthRoutes = new(StringComparer.Ordinal)
    {
        "Auth.Register",
        "Auth.Login",
        "Auth.LoginTotp",
        "Auth.PasswordResetRequest",
        "LockoutUnlock.Confirm",
    };

    public bool IsLegitimatePreAuth()
    {
        var ctx = http.HttpContext;
        if (ctx is null) return false;

        var descriptor = ctx.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (descriptor is null) return false;

        var key = $"{descriptor.ControllerName}.{descriptor.ActionName}";
        return PreAuthRoutes.Contains(key);
    }
}
