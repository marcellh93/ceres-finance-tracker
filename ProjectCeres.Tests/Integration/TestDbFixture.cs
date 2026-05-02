using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Shared setup for integration tests. Each test class that uses this fixture gets
/// a fresh AppDbContext connected to project_ceres_test. Each test is wrapped in a
/// transaction that rolls back on dispose — no test leaves data behind.
/// </summary>
public class TestDbFixture : IAsyncDisposable
{
    private const string TestConnectionString =
        "Host=localhost;Database=project_ceres_test;Username=postgres;Password=postgres";

    public AppDbContext Db { get; }
    private IDbContextTransaction? _transaction;

    public TestDbFixture()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestConnectionString)
            .AddInterceptors(new UserOwnershipInterceptor(new SingleUserAccessor()))
            .Options;

        Db = new AppDbContext(options);
    }

    /// <summary>
    /// Call at the start of each test to migrate the schema (idempotent) and open a
    /// transaction that will be rolled back when the test finishes.
    /// </summary>
    public async Task InitAsync()
    {
        await Db.Database.MigrateAsync();
        _transaction = await Db.Database.BeginTransactionAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync();

        await Db.DisposeAsync();
    }
}
