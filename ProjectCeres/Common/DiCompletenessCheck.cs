using System.Reflection;

namespace ProjectCeres.Common;

/// <summary>
/// Stage 13.a (spec D5) — walks every constructor parameter of every concrete,
/// non-abstract, public class in the scanned namespaces and confirms the DI
/// container can resolve it. Converts a silent scan-miss (wrong namespace, an
/// interface nobody scanned) into one named, immediate failure instead of a
/// runtime NullReferenceException or InvalidOperationException far from the
/// actual mistake. Run both at real app startup (Program.cs) and as a fast
/// automated test (DiCompletenessTests.cs).
/// </summary>
public static class DiCompletenessCheck
{
    private static readonly string[] ScannedNamespacePrefixes =
    [
        "ProjectCeres.Services",
    ];

    private static readonly string[] ScannedExactNamespaces =
    [
        "ProjectCeres.Admin",
        "ProjectCeres.Common.Authentication",
        "ProjectCeres.Common",
        "ProjectCeres.Common.Email",
    ];

    // Spec §3's named non-scannable list (2026-09-27 design doc) — the three
    // IEmailService branches of Program.cs's mutually-exclusive environment conditional
    // (~line 320). Only one branch's AddXxx call ever runs per environment, and this
    // check always runs under the Testing environment (no Email:Resend:ApiKey, not
    // E2E), which activates the LogOnlyEmailService branch — so the other two branches'
    // classes are both real, legitimately-scannable services, just never constructed in
    // THIS environment, for two different reasons:
    //   - FileSinkEmailService (E2E branch): constructed via a factory lambda
    //     (`new FileSinkEmailService(sinkDir, ...)`) supplying `directory` from
    //     configuration, never from DI — its constructor can never resolve under ANY
    //     environment, scanned or not.
    //   - ResendEmailService (Production branch, real API key configured): fully
    //     DI-resolvable (IResend, IOptions<EmailOptions>, ILogger<T>) in an environment
    //     where `AddResend(...)` actually runs — but Testing never calls AddResend, so
    //     IResend is unregistered here and only here.
    // Found by this check during Stage 13.a Task 2.
    private static readonly HashSet<Type> ConditionallyConstructedExclusions =
    [
        typeof(Email.FileSinkEmailService),
        typeof(Email.ResendEmailService),
    ];

    /// <summary>
    /// Returns a list of (type, missing dependency type) pairs for every scanned
    /// class whose constructor dependency cannot be resolved from <paramref name="services"/>.
    /// Empty list means the DI graph is complete for every scanned class.
    /// </summary>
    public static List<(Type Type, Type MissingDependency)> FindUnresolvable(IServiceProvider services)
    {
        var failures = new List<(Type, Type)>();

        // Exception-derived types (e.g. ProjectCeres.Services.DuplicateBudgetException),
        // record DTOs (e.g. ProjectCeres.Services.DashboardData), and Attribute subclasses
        // (e.g. ProjectCeres.Common.PreAuthCallSiteAttribute) live inside scanned namespaces
        // but are never DI services — Program.cs's Scan(...) rules exclude all three via
        // the same ScanExclusionPredicates this check uses (found by this check during
        // Stage 13.a Task 2), so a fix to one can never silently diverge from the other.
        var scannedTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic
                     && !ScanExclusionPredicates.IsExceptionType(t)
                     && !ScanExclusionPredicates.IsAttributeType(t)
                     && !ScanExclusionPredicates.IsRecordType(t)
                     && !ConditionallyConstructedExclusions.Contains(t)
                     && !ScanExclusionPredicates.IsConventionalMiddleware(t))
            .Where(t => (t.Namespace != null && ScannedNamespacePrefixes.Any(p => t.Namespace == p || t.Namespace.StartsWith(p + ".", StringComparison.Ordinal)))
                     || (t.Namespace != null && ScannedExactNamespaces.Contains(t.Namespace)));

        using var scope = services.CreateScope();

        foreach (var type in scannedTypes)
        {
            // GetConstructors() only returns PUBLIC constructors, so a class with none
            // (e.g. ProjectCeres.Common.Authentication.PreAuthUserScope — internal/private
            // ctors only, never DI-constructed) naturally falls into this `continue` rather
            // than reporting a false failure. Program.cs's Scan(...) rules still need their
            // own IsConstructibleByDi exclusion for this class because ASP.NET Core's
            // ValidateOnBuild (Development-only) validates registered descriptors directly,
            // bypassing this check entirely — found by the Step 7 full-suite run, not this
            // test, during Stage 13.a Task 2.
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is null) continue;

            foreach (var param in ctor.GetParameters())
            {
                try
                {
                    var resolved = scope.ServiceProvider.GetService(param.ParameterType);
                    if (resolved is null && !IsOptionalDependency(param))
                    {
                        failures.Add((type, param.ParameterType));
                    }
                }
                catch (Exception)
                {
                    failures.Add((type, param.ParameterType));
                }
            }
        }

        return failures;
    }

    private static bool IsOptionalDependency(ParameterInfo param) =>
        param.HasDefaultValue || Nullable.GetUnderlyingType(param.ParameterType) is not null;
}
