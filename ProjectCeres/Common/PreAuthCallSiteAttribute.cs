namespace ProjectCeres.Common;

/// <summary>
/// Marks an MVC action as a legitimate pre-authentication call site. Stage 7.6.7 / ADR-0073.
///
/// <para>
/// Replaces the prior stringly-typed <c>IPreAuthCallSiteTagger</c> registry. The
/// <see cref="HttpContextCurrentUserAccessor"/> reads <c>Endpoint.Metadata</c> for this
/// attribute and, when present, resolves the current request as
/// <see cref="UserContext.PreAuth"/> instead of warning that the user is unset.
/// </para>
///
/// <para>
/// An architecture test asserts that every <c>[AllowAnonymous]</c> action on an
/// <c>[ApiController]</c> carries this attribute — adding a new pre-auth route becomes a
/// compile-time addition (an attribute on the action) rather than a registry update
/// elsewhere.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PreAuthCallSiteAttribute : Attribute
{
    public string Name { get; }

    public PreAuthCallSiteAttribute(string name) => Name = name;
}
