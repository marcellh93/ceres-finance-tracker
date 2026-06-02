using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Tests.Integration.Authentication;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Boots the app wired to ceres_app (RLS-active) instead of ceres_admin, so RLS enforcement
/// is exercised through the production DI graph (Program.cs), not a hand-built context. Exposes
/// an AppDbContext bound to a specific acting user (RLS on) and an AdminDbContext (RLS bypassed)
/// for seeding. Stage 9.5b / D6.
///
/// SETUP DISCIPLINE: seed via NewAdminContext (BYPASSRLS), assert via NewAppContext(actingAs).
/// Under ceres_app an insert without an established user context is rejected (42501), and a read
/// without one returns zero rows (the RLS predicate matches no UserId when the GUC is unset).
/// </summary>
public sealed class DualContextWebApplicationFactory : AuthTestWebApplicationFactory
{
    protected override bool UseAppRoleConnection => true;

    /// <summary>
    /// An AppDbContext bound to <paramref name="actingAs"/> via IUserScope. The returned handle
    /// owns the DI scope and the user-scope binding; dispose it (await using) after the context.
    /// The acting user must stay bound until the connection opens lazily on first query — hence
    /// the bundled handle rather than a bare context.
    /// </summary>
    public ActingAppContext NewAppContext(Guid actingAs)
    {
        var scope = Services.CreateScope();
        // Order matters: enter the scope BEFORE resolving AppDbContext so the scoped
        // ICurrentUserAccessor injected into the context reads actingAs (IdorIsolationTests:422).
        var userScopeHandle = scope.ServiceProvider.GetRequiredService<IUserScope>().EnterAs(actingAs);
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new ActingAppContext(scope, userScopeHandle, context);
    }

    /// <summary>An AdminDbContext (ceres_admin / BYPASSRLS) for cross-user seeding and cleanup.</summary>
    public AdminContextHandle NewAdminContext()
    {
        var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        return new AdminContextHandle(scope, context);
    }

    /// <summary>
    /// Owns an AppDbContext plus the DI scope and the IUserScope binding that keep its acting
    /// user alive until the lazily-opened connection sets the RLS GUC. Use <c>handle.Context</c>.
    /// </summary>
    public sealed class ActingAppContext(IServiceScope scope, IDisposable userScopeHandle, AppDbContext context)
        : IAsyncDisposable
    {
        public AppDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            userScopeHandle.Dispose();
            scope.Dispose();
        }
    }

    /// <summary>Owns an AdminDbContext plus its DI scope. Use <c>handle.Context</c>.</summary>
    public sealed class AdminContextHandle(IServiceScope scope, AdminDbContext context) : IAsyncDisposable
    {
        public AdminDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            scope.Dispose();
        }
    }
}
