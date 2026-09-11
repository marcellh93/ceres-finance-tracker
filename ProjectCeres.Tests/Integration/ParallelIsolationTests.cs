using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Tests.Integration.Authentication;
using Xunit;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 12.18 self-tests for the DB-per-bucket mechanism itself (<see cref="TestDatabaseRouter"/>,
/// <see cref="TestWebApplicationFactory.UseDatabase"/>, <see cref="IBucketDatabase"/>). Deliberately
/// NOT a member of any <c>[Collection("IntegrationParallelK")]</c> — these tests reach ACROSS
/// buckets, which a bucketed test class (pinned to exactly one bucket's database via
/// <see cref="IntegrationTestBase{TFactory}"/>) cannot do. Instead each test constructs its own
/// ad-hoc <see cref="TestWebApplicationFactory"/> instances and pins them directly via
/// <see cref="TestWebApplicationFactory.UseDatabase"/>.
///
/// Two of the three tests only mean something when clones are provisioned
/// (<see cref="TestDatabaseRouter.CloneCount"/> &gt;= 2); the third is the fallback's mirror
/// image. Gating on <c>CloneCount</c> rather than skipping via <c>[Fact(Skip=...)]</c> keeps
/// exactly one set green in any given environment — a local `dotnet test` with no clones
/// provisioned still passes, it just proves the fallback-collapse instead of the isolation.
/// </summary>
public class ParallelIsolationTests
{
    /// <summary>
    /// Router-level distinctness: two bucket names must resolve to two different database
    /// names, and the collection-fixture holder types built on top of the router
    /// (<see cref="Bucket1Database"/>, <see cref="Bucket2Database"/>) must agree.
    /// </summary>
    [Fact]
    public void Two_buckets_resolve_distinct_databases()
    {
        if (TestDatabaseRouter.CloneCount < 2) return;

        var db1 = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
        var db2 = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
        db1.Should().NotBe(db2, "each parallel bucket must own a distinct database");

        var bucket1 = new Bucket1Database();
        var bucket2 = new Bucket2Database();
        bucket1.DatabaseName.Should().NotBe(bucket2.DatabaseName,
            "the collection-fixture holders must reflect the same distinctness the router gives");
    }

    /// <summary>
    /// The load-bearing isolation proof: a user written into bucket 1's database must be
    /// completely absent from bucket 2's database. Isolation here is by separate PHYSICAL
    /// database (distinct Npgsql pool per bucket clone), not a query filter — Identity's
    /// AspNetUsers is deliberately unfiltered (cross-tenant by definition), so the
    /// IgnoreQueryFilters() below is a belt-and-suspenders no-op, not the thing that isolates.
    /// </summary>
    [Fact]
    public async Task A_user_written_in_bucket1_is_absent_in_bucket2()
    {
        if (TestDatabaseRouter.CloneCount < 2) return;

        var bucket1Db = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
        var bucket2Db = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");

        await using var factory1 = new TestWebApplicationFactory();
        factory1.UseDatabase(bucket1Db);
        await using var factory2 = new TestWebApplicationFactory();
        factory2.UseDatabase(bucket2Db);

        var email = $"iso-{Guid.NewGuid()}@bucket-iso.local";
        var user = await AuthTestFixture.RegisterUserAsync(factory1, email);

        try
        {
            using var scope1 = factory1.Services.CreateScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
            var presentInBucket1 = await db1.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Email == email);
            presentInBucket1.Should().BeTrue(
                "the user must exist in the bucket it was registered into, or this test proves nothing");

            using var scope2 = factory2.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var presentInBucket2 = await db2.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Email == email);
            presentInBucket2.Should().BeFalse(
                "a user written in bucket 1 must not be visible from bucket 2's database at all");
        }
        finally
        {
            using var cleanupScope = factory1.Services.CreateScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var um = cleanupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await UserOwnedCleanup.PurgeUserAsync(db, user.Id);
            await um.DeleteAsync(user);
        }
    }

    /// <summary>
    /// The complement of the two tests above: under the N=1 fallback (no clones
    /// provisioned), every bucket name — including a "serial" collection name that isn't
    /// even in the IntegrationParallelK shape — must collapse onto the single legacy
    /// database, because that's what makes xUnit's serialized execution safe in that mode.
    /// </summary>
    [Fact]
    public void N1_fallback_collapses_all_buckets_to_legacy()
    {
        if (TestDatabaseRouter.CloneCount >= 2) return;

        var db1 = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
        var db3 = TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");

        db1.Should().Be(TestDatabaseRouter.LegacyDatabase);
        db3.Should().Be(TestDatabaseRouter.LegacyDatabase);
        db1.Should().Be(db3, "every bucket must collapse onto the same database under the fallback");
    }
}
