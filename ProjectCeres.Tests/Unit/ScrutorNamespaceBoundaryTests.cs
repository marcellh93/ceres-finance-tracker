using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using Scrutor;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Stage 13.a (spec §8) -- proves the scan's namespace boundaries are real,
/// not accidentally matching everything in the assembly.
/// </summary>
public class ScrutorNamespaceBoundaryTests
{
    // A throwaway class OUTSIDE every scanned namespace, with an interface --
    // must never be picked up regardless of its naming or interface shape.
    private interface IOutsideScanScope { }
    private sealed class OutsideScanScope : IOutsideScanScope { }

    [Fact]
    public void Class_outside_scanned_namespaces_is_not_registered()
    {
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes.InNamespaces("ProjectCeres.Services"))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Any(d => d.ServiceType == typeof(IOutsideScanScope)).Should().BeFalse(
            "this test's own interface/class pair is declared in ProjectCeres.Tests.Unit, " +
            "never ProjectCeres.Services -- it must not be swept in");
    }

    [Fact]
    public void Class_in_CommonExceptions_sibling_namespace_is_not_registered_by_the_Common_scan()
    {
        // Mirrors the real risk spec §3b found: a bare InNamespaces("ProjectCeres.Common")
        // would sweep in ProjectCeres.Common.Exceptions. This proves InExactNamespaces
        // does NOT make that mistake, using this test file's own throwaway type instead
        // of a real exception class (never commit a throwaway type into ProjectCeres/).
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes.InExactNamespaces("ProjectCeres.Common"))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Any(d => d.ServiceType == typeof(IOutsideScanScope)).Should().BeFalse(
            "InExactNamespaces(\"ProjectCeres.Common\") must not match a class in a " +
            "different namespace even if that namespace starts with \"ProjectCeres.Common.\"");
    }

    [Fact]
    public void RegisterAsSingleton_attribute_changes_lifetime_from_the_Scoped_default()
    {
        // Spec §8's last bullet: confirm the attribute actually changes lifetime for a
        // real case. TokenLookupHasher carries [RegisterAsSingleton] as of Task 5.
        //
        // CORRECTION (discovered during Task 2, confirmed via reflection against the
        // real installed Scrutor 7.0.0 DLL -- not assumed): UsingLifetimeFactory does
        // not exist anywhere on Scrutor 7.0.0's ILifetimeSelector. The only members are
        // the three fixed-lifetime terminals WithSingletonLifetime()/WithScopedLifetime()/
        // WithTransientLifetime() -- no overload takes a Func<Type, ServiceLifetime>.
        // Program.cs's real Scan(...) calls (since Task 2) use two separate AddClasses
        // batches instead -- one filtered to [RegisterAsSingleton] classes terminating in
        // WithSingletonLifetime(), one filtered to everything else terminating in
        // WithScopedLifetime(). This test reproduces that exact real shape instead of the
        // nonexistent single-call API the original brief draft assumed.
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes
                .InExactNamespaces("ProjectCeres.Common.Authentication")
                .Where(t => t.IsDefined(typeof(RegisterAsSingletonAttribute), inherit: false)))
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsSelf()
            .WithSingletonLifetime()
            .AddClasses(classes => classes
                .InExactNamespaces("ProjectCeres.Common.Authentication")
                .Where(t => !t.IsDefined(typeof(RegisterAsSingletonAttribute), inherit: false)))
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsSelf()
            .WithScopedLifetime());

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ProjectCeres.Common.Authentication.TokenLookupHasher));
        descriptor.Should().NotBeNull("TokenLookupHasher must be scanned as a self-registered concrete");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton,
            "TokenLookupHasher carries [RegisterAsSingleton], which must override the Scoped default");
    }
}
