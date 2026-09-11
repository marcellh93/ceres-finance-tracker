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
//
// Stage 12.18 (final-review fix v2, 2026-09-11) — the pin must be in place BEFORE the
// factory's host builds, and a bucketed test class builds the host from a FIELD
// INITIALIZER (`private readonly HttpClient _client = factory.CreateClient();`).
// C# runs a derived class's field initializers BEFORE the base constructor body
// (verified empirically) — so IntegrationTestBase's ctor call to UseDatabase(...) runs
// AFTER CreateClient() has already latched the host against the default (legacy) DB,
// and the pin throws "factory already built targeting project_ceres_test". This is
// intra-class: the FIRST bucketed class to construct fails on its own field
// initializer, so DisableParallelization does not help.
//
// The fix removes the ordering dependency entirely: each bucket owns pinned factory
// SUBCLASSES (Bucket{K}Factory / Bucket{K}AuthFactory) whose InitDbName already IS the
// bucket's database. A bucketed class injects its bucket's subclass, so the field
// initializer's CreateClient() builds directly against the bucket DB regardless of
// construction order. IntegrationTestBase's UseDatabase(bucket.DatabaseName) is then a
// same-DB no-op — kept as a defence-in-depth assertion that the injected factory and
// the injected IBucketDatabase agree. ICollectionFixture resolving by exact type
// (the reason a single shared factory type was used originally) is exactly what makes
// distinct per-bucket subclass types safe: each collection provides its own subclass,
// and a test class in that collection injects that subclass by name.
//
// Under the N=1 fallback (CloneCount < 2) every Bucket{K}* subclass's InitDbName and
// every Bucket{K}Database resolve to the legacy DB, so the four collections' factories
// all target project_ceres_test — identical to the pre-12.18 serial behaviour, and
// still self-consistent (no-op pin). DisableParallelization is retained so same-bucket
// classes never build peer clones concurrently under a partially-warmed pool.

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

// Stage 12.18 (final-review fix) — SweepingTestWebApplicationFactory (base type) never had
// its InitDbName overridden, so every bucket's sweep-on-dispose ran against the LEGACY
// database instead of that bucket's own clone — the sweep could delete users a sibling
// bucket (sharing the legacy DB under N=1, or racing under clones) was still using, and
// under real clones it swept nothing in the bucket DB at all. One sealed subclass per
// bucket, each pinned via InitDbName, fixes both: the base type stays available for any
// non-bucket use.
public sealed class Bucket1SweepingFactory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
}

public sealed class Bucket2SweepingFactory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
}

public sealed class Bucket3SweepingFactory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");
}

public sealed class Bucket4SweepingFactory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4");
}

// Stage 12.18 (final-review fix v2) — per-bucket pinned factory subclasses. InitDbName
// already IS the bucket's database, so the host builds against the bucket DB even when a
// test class's field initializer (`= factory.CreateClient()`) runs before the base ctor.
// One plain + one auth subclass per bucket; a bucketed test class injects its bucket's
// pair and inherits IntegrationTestBase<Bucket{K}Factory> / <Bucket{K}AuthFactory>.
public sealed class Bucket1Factory : TestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
}

public sealed class Bucket2Factory : TestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
}

public sealed class Bucket3Factory : TestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");
}

public sealed class Bucket4Factory : TestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4");
}

public sealed class Bucket1AuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
}

public sealed class Bucket2AuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
}

public sealed class Bucket3AuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");
}

public sealed class Bucket4AuthFactory : AuthTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4");
}

// DisableParallelization=true: makes the classes WITHIN this one collection run
// sequentially against each other (xunit.net: "Any test which is opted out of
// parallelism will be guaranteed not to run in parallel against any other test" in
// its own collection) — required so exactly one class at a time can pin the bucket's
// shared TestWebApplicationFactory/AuthTestWebApplicationFactory/SweepingFactory
// before any sibling class's field initializer can trigger their lazy host build.
// The four IntegrationParallelK collections still run CONCURRENTLY with each other
// (this flag is per-collection, not global) — that cross-bucket parallelism is the
// actual Stage 12.18 speedup; only same-bucket classes serialize.
[CollectionDefinition("IntegrationParallel1", DisableParallelization = true)]
public class IntegrationParallel1Collection
    : ICollectionFixture<Bucket1Factory>,
      ICollectionFixture<Bucket1AuthFactory>,
      ICollectionFixture<Bucket1SweepingFactory>,
      ICollectionFixture<Bucket1Database>
{ }

[CollectionDefinition("IntegrationParallel2", DisableParallelization = true)]
public class IntegrationParallel2Collection
    : ICollectionFixture<Bucket2Factory>,
      ICollectionFixture<Bucket2AuthFactory>,
      ICollectionFixture<Bucket2SweepingFactory>,
      ICollectionFixture<Bucket2Database>
{ }

[CollectionDefinition("IntegrationParallel3", DisableParallelization = true)]
public class IntegrationParallel3Collection
    : ICollectionFixture<Bucket3Factory>,
      ICollectionFixture<Bucket3AuthFactory>,
      ICollectionFixture<Bucket3SweepingFactory>,
      ICollectionFixture<Bucket3Database>
{ }

[CollectionDefinition("IntegrationParallel4", DisableParallelization = true)]
public class IntegrationParallel4Collection
    : ICollectionFixture<Bucket4Factory>,
      ICollectionFixture<Bucket4AuthFactory>,
      ICollectionFixture<Bucket4SweepingFactory>,
      ICollectionFixture<Bucket4Database>
{ }
