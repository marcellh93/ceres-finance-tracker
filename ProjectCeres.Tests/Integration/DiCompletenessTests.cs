using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
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

    [Fact]
    public void Both_RecentAuth_and_AdminLive_authorization_handlers_are_registered()
    {
        // RecentAuthRequirementHandler and AdminLiveRequirementHandler both implement
        // IAuthorizationHandler (the second via inheriting AuthorizationHandler<TRequirement>)
        // and ASP.NET Core resolves IEnumerable<IAuthorizationHandler> to run EVERY
        // registered handler, by design -- this is not a last-registration-wins
        // interface, unlike every other scanned interface in this project. Found by
        // Task 6's implementer BEFORE any removal was attempted (reproduced against the
        // real Scrutor 7.0.0 DLL): RegistrationStrategy.Skip calls IServiceCollection
        // .TryAdd, keyed purely by ServiceType, so a plain interface-pair scan rule
        // would silently drop the second-enumerated handler entirely -- no error, no
        // warning, just a security check that stops firing. Neither
        // Every_scanned_class_constructor_dependency_resolves (proves A dependency
        // resolves, not a COUNT) nor a hypothetical single-instance assertion can catch
        // this regression; only an explicit count/identity check on the IEnumerable can.
        using var scope = Factory.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToList();

        handlers.Should().Contain(h => h is RecentAuthRequirementHandler,
            because: "RecentAuthRequirementHandler gates the [RequireRecentAuth] policy; " +
                     "a RegistrationStrategy.Skip regression would silently drop whichever " +
                     "of the two handlers is enumerated second");
        handlers.Should().Contain(h => h is AdminLiveRequirementHandler,
            because: "AdminLiveRequirementHandler gates the AdminLive policy via a live " +
                     "DB role check -- losing this handler silently disables that check " +
                     "with no error, not merely a DI-wiring inconvenience");
    }

    [Fact]
    public void RecentAuthMiddlewareResultHandler_wins_over_the_framework_default()
    {
        // Found by the controller via a live reflection probe during Task 6, not assumed:
        // AddControllersWithViews() (Program.cs, called BEFORE either Scan(...) call)
        // internally calls AddAuthorization(), whose AddAuthorizationPolicyEvaluator() does
        // services.TryAddTransient<IAuthorizationMiddlewareResultHandler,
        // AuthorizationMiddlewareResultHandler>() (confirmed against dotnet/aspnetcore's real
        // source, PolicyServiceCollectionExtensions.cs) -- the FRAMEWORK DEFAULT claims this
        // interface slot before the scan ever runs. RegistrationStrategy.Skip (TryAdd, keyed
        // by ServiceType) is therefore a guaranteed no-op for RecentAuthMiddlewareResultHandler
        // specifically, regardless of scan ordering -- a regression Every_scanned_class_
        // constructor_dependency_resolves cannot catch, since the framework default also
        // implements the interface and also "resolves" a non-null instance. The original
        // manual registration (plain AddSingleton, never TryAdd) always won unconditionally;
        // the scan must use RegistrationStrategy.Replace() to replicate that.
        using var scope = Factory.Services.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        handler.Should().BeOfType<RecentAuthMiddlewareResultHandler>(
            because: "the framework's own AuthorizationMiddlewareResultHandler default " +
                     "returns 403 Forbidden on a failed policy, silently reverting the " +
                     "custom 401 REAUTH_REQUIRED envelope every [RequireRecentAuth] " +
                     "endpoint depends on");
    }
}
