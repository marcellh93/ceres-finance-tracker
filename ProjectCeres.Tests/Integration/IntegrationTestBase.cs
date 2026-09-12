using System.Net.Http;

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

    /// <summary>
    /// A default <see cref="HttpClient"/> for the pinned factory, built in this base
    /// constructor AFTER the database pin. Derived classes use this instead of capturing
    /// the injected <c>factory</c> into their own field: capturing it there (e.g.
    /// <c>private readonly HttpClient _client = factory.CreateClient();</c>) makes the
    /// primary-constructor parameter live in TWO places — the base (via <see cref="Factory"/>)
    /// and the derived field — which is compiler warning CS9107 (a real double-capture hazard;
    /// the idiomatic fix is one source of truth in the base). Building it here also removes the
    /// C# ordering footgun that a derived FIELD initializer runs BEFORE this constructor: the
    /// field's <c>CreateClient()</c> would build the host before the pin lands. Here the client
    /// is created after <c>UseDatabase</c>, always against the correct bucket DB.
    ///
    /// A class needing a non-default client (e.g. <c>HandleCookies = false</c>) still calls
    /// <c>Factory.CreateClient(options)</c> from a method or its own constructor body — that
    /// is a method use of <see cref="Factory"/>, not a captured parameter, so it does not warn.
    /// </summary>
    protected HttpClient Client { get; }

    protected IntegrationTestBase(TFactory factory, IBucketDatabase bucket)
    {
        Factory = factory;
        factory.UseDatabase(bucket.DatabaseName);
        Client = factory.CreateClient();
    }
}
