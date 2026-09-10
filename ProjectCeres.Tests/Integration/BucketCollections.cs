using Xunit;

namespace ProjectCeres.Tests.Integration;

// Stage 12.18 — one sweeping factory subclass per bucket, each pinned to its bucket
// database via TestDatabaseRouter. Sweeping is inherited from
// SweepingTestWebApplicationFactory; because the factory's DI resolves AppDbContext
// from DatabaseName, the sweep scopes to THIS bucket's DB automatically.
public sealed class Bucket1Factory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel1");
}

public sealed class Bucket2Factory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel2");
}

public sealed class Bucket3Factory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel3");
}

public sealed class Bucket4Factory : SweepingTestWebApplicationFactory
{
    protected override string InitDbName => TestDatabaseRouter.DatabaseForCollection("IntegrationParallel4");
}

[CollectionDefinition("IntegrationParallel1")]
public class IntegrationParallel1Collection : ICollectionFixture<Bucket1Factory> { }

[CollectionDefinition("IntegrationParallel2")]
public class IntegrationParallel2Collection : ICollectionFixture<Bucket2Factory> { }

[CollectionDefinition("IntegrationParallel3")]
public class IntegrationParallel3Collection : ICollectionFixture<Bucket3Factory> { }

[CollectionDefinition("IntegrationParallel4")]
public class IntegrationParallel4Collection : ICollectionFixture<Bucket4Factory> { }
