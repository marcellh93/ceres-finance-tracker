using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 13.a (spec D5, §8) — the fast automated half of the DI-completeness
/// check. Boots the real DI graph via TestWebApplicationFactory and asserts
/// every scanned class's constructor dependencies resolve.
/// </summary>
[Collection("IntegrationParallel3")]
public class DiCompletenessTests : IntegrationTestBase<Bucket3Factory>
{
    public DiCompletenessTests(Bucket3Factory factory, Bucket3Database bucketDb) : base(factory, bucketDb) { }

    [Fact]
    public void Every_scanned_class_constructor_dependency_resolves()
    {
        var failures = DiCompletenessCheck.FindUnresolvable(Factory.Services);

        failures.Should().BeEmpty(
            because: "a Scrutor scan miss should fail here, with the exact type named, " +
                     "not as a scattered runtime failure in an unrelated test");
    }
}
