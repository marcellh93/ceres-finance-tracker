using Xunit;

namespace ProjectCeres.Tests.Integration;

// Stage 12.18 — DB-per-bucket infra. Four spikes (all reverted; see the design doc
// addendum) established that a per-bucket factory SUBCLASS cannot work on xUnit
// 2.5.3: ICollectionFixture<T> resolves a test class's constructor parameter by
// EXACT type, never by assignability, and no v2 hook varies a same-typed fixture's
// DB per collection. The working mechanism instead:
//   1. Each bucket collection supplies the plain (non-subclassed) factory types a
//      bucketed test may inject, plus a distinct IBucketDatabase holder type.
//   2. A bucketed test class inherits IntegrationTestBase<TFactory>, which calls
//      factory.UseDatabase(bucket.DatabaseName) before the factory's host builds.
// Distinct holder TYPES (not just distinct values) are required because
// ICollectionFixture is resolved by type — a single IBucketDatabase implementation
// parameterized by bucket number would collide across all four collections.

public interface IBucketDatabase
{
    string DatabaseName { get; }
}

public sealed class Bucket1Database : IBucketDatabase
{
    public string DatabaseName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
}

public sealed class Bucket2Database : IBucketDatabase
{
    public string DatabaseName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
}

public sealed class Bucket3Database : IBucketDatabase
{
    public string DatabaseName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");
}

public sealed class Bucket4Database : IBucketDatabase
{
    public string DatabaseName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4");
}

[CollectionDefinition("IntegrationParallel1")]
public class IntegrationParallel1Collection
    : ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>,
      ICollectionFixture<SweepingTestWebApplicationFactory>,
      ICollectionFixture<Bucket1Database>
{ }

[CollectionDefinition("IntegrationParallel2")]
public class IntegrationParallel2Collection
    : ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>,
      ICollectionFixture<SweepingTestWebApplicationFactory>,
      ICollectionFixture<Bucket2Database>
{ }

[CollectionDefinition("IntegrationParallel3")]
public class IntegrationParallel3Collection
    : ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>,
      ICollectionFixture<SweepingTestWebApplicationFactory>,
      ICollectionFixture<Bucket3Database>
{ }

[CollectionDefinition("IntegrationParallel4")]
public class IntegrationParallel4Collection
    : ICollectionFixture<TestWebApplicationFactory>,
      ICollectionFixture<AuthTestWebApplicationFactory>,
      ICollectionFixture<SweepingTestWebApplicationFactory>,
      ICollectionFixture<Bucket4Database>
{ }
