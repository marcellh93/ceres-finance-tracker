using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 13.a (spec D5, §8) — the fast automated half of the DI-completeness
/// check. Boots the real DI graph via TestWebApplicationFactory and asserts
/// every scanned class's constructor dependencies resolve.
/// </summary>
[Collection("IntegrationParallel3")]
public class DiCompletenessTests : IntegrationTestBase<Bucket3Factory>
{
    public DiCompletenessTests(Bucket3Factory factory, Bucket3Database bucketDb) : base(factory, bucketDb) { }

    [Fact]
    public void Every_scanned_class_constructor_dependency_resolves()
    {
        var failures = DiCompletenessCheck.FindUnresolvable(Factory.Services);

        failures.Should().BeEmpty(
            because: "a Scrutor scan miss should fail here, with the exact type named, " +
                     "not as a scattered runtime failure in an unrelated test");
    }

    [Fact]
    public void Argon2idPasswordHasher_interface_and_concrete_resolutions_are_the_same_instance()
    {
        // Spec §3d's dual-consumer exclusion claims IPasswordHasher<ApplicationUser>
        // consumers (e.g. ASP.NET Identity's UserManager) and the 13+ classes that
        // inject the concrete Argon2idPasswordHasher type directly are satisfied by
        // ONE registration, not two silently-coexisting ones. Every_scanned_class_
        // constructor_dependency_resolves above cannot detect that: GetService per
        // parameter type proves each dependency resolves to SOMETHING, not that both
        // resolution paths reach the SAME something -- a regression that re-adds a
        // second, conflicting AddScoped<Argon2idPasswordHasher>() alongside the scan's
        // AsSelfWithInterfaces() rule would pass that test while silently violating
        // this claim (RegistrationStrategy.Skip would make one of the two inert,
        // depending on registration order -- never asserted anywhere until now).
        using var scope = Factory.Services.CreateScope();

        var viaInterface = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var viaConcreteType = scope.ServiceProvider.GetRequiredService<Argon2idPasswordHasher>();

        viaInterface.Should().BeSameAs(viaConcreteType,
            because: "AsSelfWithInterfaces() must resolve both consumers to one shared " +
                     "instance -- two separate instances would mean two registrations " +
                     "silently coexist, which Every_scanned_class_constructor_dependency_" +
                     "resolves cannot detect");
    }
}
