using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;

namespace ProjectCeres.Tests.Unit;

public class ErasureLaneCoverageTests
{
    private static AppDbContext Ctx() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options,
        new Common.FakeCurrentUserAccessor(Guid.Empty));

    [Fact]
    public void Every_user_content_entity_is_in_exactly_one_lane()
    {
        var model = Ctx().Model;
        var lanes = ErasureLanes.Classify(model);
        var all = UserContentEntities.List(model).Select(t => t.PostgresTableName).ToHashSet();
        var covered = lanes.Purge.Concat(lanes.Statutory).Concat(lanes.Support).ToList();

        covered.Should().OnlyHaveUniqueItems("no entity in two lanes");
        covered.ToHashSet().Should().BeEquivalentTo(all, "every content entity has exactly one fate");
        lanes.Statutory.Should().Contain("Transactions").And.Contain("Accounts").And.Contain("Categories");
        lanes.Support.Should().Contain("SupportTickets");
        lanes.Purge.Should().Contain("SavedReports");
    }
}
