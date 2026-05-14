using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProjectCeres.Common;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Resolves <see cref="UserContext"/> from the ambient HTTP context, falling back to the
/// background-job <see cref="IUserScope"/>. Stage 7.6.7 / ADR-0073 — replaces the prior
/// <c>Guid.Empty</c>-as-overload accessor.
///
/// <para>Resolution order:</para>
/// <list type="number">
///   <item>HTTP cookie claim (<see cref="ClaimTypes.NameIdentifier"/>) parses to a Guid →
///         <see cref="UserContext.Resolved"/>.</item>
///   <item>Otherwise, <see cref="IUserScope.Current"/> set →
///         <see cref="UserContext.Resolved"/> (background job that entered via
///         <see cref="IBackgroundJobScope"/>).</item>
///   <item>Otherwise, the current endpoint metadata carries
///         <see cref="PreAuthCallSiteAttribute"/> → <see cref="UserContext.PreAuth"/>.</item>
///   <item>Otherwise, an HTTP context exists but no user has been resolved →
///         <see cref="UserContext.Background"/> with a reason describing where this came
///         from. Useful for tracking down "request landed without auth and isn't
///         tagged" cases.</item>
///   <item>No HTTP context, no scope, no endpoint → <see cref="UserContext.Uninitialized"/>.
///         This case fires at EF model-creation time (the global query filter expression is
///         evaluated eagerly, before any HTTP context is established) and during test
///         bootstrap.</item>
/// </list>
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor http, IUserScope scope) : ICurrentUserAccessor
{
    public UserContext Context
    {
        get
        {
            var ctx = http.HttpContext;

            var claim = ctx?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(claim, out var fromCookie))
                return new UserContext.Resolved(fromCookie);

            if (scope.Current is { } fromScope)
                return new UserContext.Resolved(fromScope);

            if (ctx is null)
                return UserContext.Uninitialized.Instance;

            var preAuth = ctx.GetEndpoint()?.Metadata.GetMetadata<PreAuthCallSiteAttribute>();
            if (preAuth is not null)
                return new UserContext.PreAuth(preAuth.Name);

            // HTTP context present but no auth, no scope, no [PreAuthCallSite] tag.
            // Either an [Authorize] endpoint hit by an unauthenticated client (the
            // pipeline will return 401 before any service code runs, so this is
            // observational) or an [AllowAnonymous] endpoint that should have been
            // tagged. Surfaces as Background so the RowLevelSecurityInterceptor logs
            // a single error per such request rather than the prior tagger's per-pool
            // warning noise.
            var endpoint = ctx.GetEndpoint()?.DisplayName ?? "(no endpoint)";
            return new UserContext.Background($"HTTP request reached the DB without auth or [PreAuthCallSite]. Endpoint: {endpoint}");
        }
    }
}
