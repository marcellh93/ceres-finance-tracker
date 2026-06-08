using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class MigrationDriftTests
{
    // Building DbContextOptions + reading model metadata only triggers OnModelCreating;
    // HasPendingModelChanges diffs the model against AppDbContextModelSnapshot.cs — no
    // connection is opened, so a placeholder Npgsql connection string is fine.
    // (Same pattern as UserOwnedModelTests.Ctx().)
    private static AppDbContext Ctx() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new ProjectCeres.Tests.Common.FakeCurrentUserAccessor(System.Guid.Empty));

    [Fact]
    public void Model_has_no_pending_migration_changes()
    {
        // Condition E3 (Stage 9.5e): an entity-shape change must ship its migration in the
        // same commit. HasPendingModelChanges() returns true when the EF model has drifted
        // from the last migration snapshot.
        using var db = Ctx();
        db.Database.HasPendingModelChanges()
            .Should().BeFalse(
                "an entity's shape changed without a matching migration — run " +
                "`dotnet ef migrations add <Name>` in the same commit (Condition E3)");
    }
}
