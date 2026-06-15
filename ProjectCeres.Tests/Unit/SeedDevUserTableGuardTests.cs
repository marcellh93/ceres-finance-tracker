using FluentAssertions;
using ProjectCeres.Tools;

namespace ProjectCeres.Tests.Unit;

public class SeedDevUserTableGuardTests
{
    [Fact]
    public void AssertKnownTable_throws_for_table_not_in_allowed_set()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Transactions", "Accounts" };

        var act = () => SeedDevUser.AssertKnownTable("Users; DROP TABLE Accounts", allowed);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not in the user-owned table allow-list*");
    }

    [Fact]
    public void AssertKnownTable_returns_the_name_for_an_allowed_table()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Transactions", "Accounts" };

        SeedDevUser.AssertKnownTable("Transactions", allowed).Should().Be("Transactions");
    }
}
