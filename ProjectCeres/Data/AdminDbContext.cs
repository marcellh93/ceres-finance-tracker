using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ProjectCeres.Common;

namespace ProjectCeres.Data;

/// <summary>
/// Cross-tenant variant of <see cref="AppDbContext"/> backed by the
/// <c>AdminConnection</c> connection string (Postgres role <c>ceres_admin</c>,
/// which has <c>BYPASSRLS</c>). Stage 7.5 / ADR-0068.
///
/// <para>
/// Identity is hard-wired to a single DbContext type via
/// <c>AddEntityFrameworkStores&lt;AppDbContext&gt;()</c>, so we model the admin
/// variant as a subclass. The subclass shares <c>OnModelCreating</c>, the entity
/// set, and the global query filters with <see cref="AppDbContext"/> — admin code
/// that needs cross-tenant reads pairs it with <c>IgnoreQueryFilters()</c> as
/// documented in the Stage 7 architecture-test allow-list.
/// </para>
///
/// <para>
/// The <see cref="RowLevelSecurityInterceptor"/> is intentionally NOT registered
/// on this DbContext — the <c>ceres_admin</c> role bypasses RLS at the database
/// level, so emitting <c>SET LOCAL "app.current_user_ref"</c> would be redundant.
/// </para>
/// </summary>
public sealed class AdminDbContext : AppDbContext
{
    public AdminDbContext(DbContextOptions<AdminDbContext> options, ICurrentUserAccessor currentUser)
        : base(Rewrap(options), currentUser)
    {
    }

    /// <summary>
    /// Re-wraps the strongly-typed <c>DbContextOptions&lt;AdminDbContext&gt;</c>
    /// into a <c>DbContextOptions&lt;AppDbContext&gt;</c> that the base ctor
    /// requires. The underlying <see cref="IDbContextOptionsExtension"/> list
    /// (provider, interceptors, model-cache key) is preserved verbatim.
    /// </summary>
    private static DbContextOptions<AppDbContext> Rewrap(DbContextOptions<AdminDbContext> options)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        foreach (var extension in options.Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }
        return builder.Options;
    }
}
