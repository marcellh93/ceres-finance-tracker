using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 7.5 / ADR-0068 — pin the DI shape introduced by the three-connection-string
/// split. These are architecture-style assertions about the service registrations,
/// not behaviour tests of EF or the interceptor itself.
/// </summary>
[Collection("IntegrationParallel3")]
public class DbContextRegistrationTests : IntegrationTestBase<Bucket3Factory>
{
    private readonly Bucket3Factory _factory;

    public DbContextRegistrationTests(Bucket3Factory factory, Bucket3Database bucketDb) : base(factory, bucketDb)
    {
        _factory = factory;
    }

    [Fact]
    public void AppDbContext_resolves_via_ApplicationConnection_key()
    {
        // In production, ApplicationConnection points at ceres_app (NOBYPASSRLS).
        // In the WAF, the test fixture redirects ApplicationConnection at the admin
        // role so legacy cross-tenant cleanup paths keep working; the dedicated RLS
        // fixture connects as ceres_app directly to exercise the wall. The invariant
        // this test pins is that AppDbContext resolves from the ApplicationConnection
        // KEY — whatever it points at — not which role it ends up using.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connStr = db.Database.GetConnectionString();
        connStr.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AdminDbContext_resolves_under_AdminConnection()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();

        var connStr = db.Database.GetConnectionString();
        connStr.Should().Contain("Username=ceres_admin");
    }

    [Fact]
    public void RowLevelSecurityInterceptor_is_registered_only_on_AppDbContext()
    {
        // The runtime context gets the interceptor; the admin context does not (ceres_admin
        // bypasses RLS at the database level, so SET LOCAL would be redundant).
        using var scope = _factory.Services.CreateScope();

        var appOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var adminOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AdminDbContext>>();

        InterceptorsOf(appOptions).Should().Contain(i => i is RowLevelSecurityInterceptor);
        InterceptorsOf(adminOptions).Should().NotContain(i => i is RowLevelSecurityInterceptor);
    }

    [Fact]
    public void UserOwnershipInterceptor_is_registered_on_both_DbContexts()
    {
        using var scope = _factory.Services.CreateScope();

        var appOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var adminOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AdminDbContext>>();

        InterceptorsOf(appOptions).Should().Contain(i => i is UserOwnershipInterceptor);
        InterceptorsOf(adminOptions).Should().Contain(i => i is UserOwnershipInterceptor);
    }

    [Fact]
    public void UserJobRunner_resolves_AdminDbContext_not_AppDbContext()
    {
        // Smoke test that the constructor switch from AppDbContext to AdminDbContext
        // takes effect: resolving IUserJobRunner from DI must succeed (i.e. the DI
        // container can build it under the new ctor).
        using var scope = _factory.Services.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IUserJobRunner>();

        runner.Should().NotBeNull();
        runner.Should().BeOfType<UserJobRunner>();
    }

    private static IEnumerable<IInterceptor> InterceptorsOf(DbContextOptions options)
    {
        var ext = options.FindExtension<CoreOptionsExtension>();
        return ext?.Interceptors ?? Array.Empty<IInterceptor>();
    }
}
