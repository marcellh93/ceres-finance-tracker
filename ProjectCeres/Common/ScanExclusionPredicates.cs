namespace ProjectCeres.Common;

/// <summary>
/// Stage 13.a — type-shape checks for classes that are reflectively ordinary public
/// concrete types in a scanned namespace but are never DI services: exceptions, record
/// DTOs, Attribute subclasses, and conventional ASP.NET Core middleware. Shared between
/// Program.cs's Scan(...) rules and DiCompletenessCheck so a fix to one can never
/// silently diverge from the other — see each call site's own comment for the discovery
/// story of each case.
/// </summary>
public static class ScanExclusionPredicates
{
    public static bool IsExceptionType(Type type) => typeof(Exception).IsAssignableFrom(type);

    // The compiler-synthesized method every C# record (class or struct) emits and
    // ordinary classes never do.
    public static bool IsRecordType(Type type) => type.GetMethod("<Clone>$") is not null;

    public static bool IsAttributeType(Type type) => typeof(Attribute).IsAssignableFrom(type);

    // Conventional ASP.NET Core middleware (registered via app.UseMiddleware&lt;T&gt;(),
    // e.g. ProjectCeres.Common.Authentication.PersistentCookieRotationMiddleware and
    // UserBlockedIpMiddleware) takes a RequestDelegate constructor parameter that the
    // middleware pipeline's own activator supplies — never the general DI container.
    // Spec §3 already treats this as "a different registration shape entirely" for
    // LanguagePreferenceMiddleware (excluded there by living in an unscanned
    // sub-namespace); these two live directly in a scanned namespace instead, so they
    // need this type-shape check rather than a namespace exclusion.
    public static bool IsConventionalMiddleware(Type type) =>
        type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(RequestDelegate)));
}
