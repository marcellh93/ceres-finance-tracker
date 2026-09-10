namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Base for bucketed integration test classes. Pins the injected <typeparamref name="TFactory"/>
/// to the bucket's database before the factory's host builds — the proven DB-per-collection
/// mechanism (see the Stage 12.18 design doc addendum for why a per-bucket factory subclass
/// does not work on xUnit 2.5.3: ICollectionFixture resolves by exact type, never
/// assignability, so all bucketed classes must share the same factory type and instead vary
/// the database through this base's constructor).
///
/// Generic so a class needing <see cref="AuthTestWebApplicationFactory"/> or
/// <see cref="SweepingTestWebApplicationFactory"/> inherits
/// <c>IntegrationTestBase&lt;AuthTestWebApplicationFactory&gt;</c> etc. and still gets the
/// same bucket-pinning behaviour.
/// </summary>
public abstract class IntegrationTestBase<TFactory> where TFactory : TestWebApplicationFactory
{
    protected readonly TFactory Factory;

    protected IntegrationTestBase(TFactory factory, IBucketDatabase bucket)
    {
        Factory = factory;
        factory.UseDatabase(bucket.DatabaseName);
    }
}
