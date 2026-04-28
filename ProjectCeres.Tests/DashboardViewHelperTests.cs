using FluentAssertions;
using ProjectCeres.Helpers;

namespace ProjectCeres.Tests;

public class DashboardViewHelperTests
{
    [Fact]
    public void RunwayCssClass_ReturnsGreen_WhenRunwayExceedsSixMonths()
    {
        DashboardViewHelper.RunwayCssClass(6.1m).Should().Be("text-green-600");
        DashboardViewHelper.RunwayCssClass(12m).Should().Be("text-green-600");
    }

    [Fact]
    public void RunwayCssClass_ReturnsAmber_WhenRunwayIsBetweenThreeAndSixMonthsInclusive()
    {
        DashboardViewHelper.RunwayCssClass(3m).Should().Be("text-amber-600");
        DashboardViewHelper.RunwayCssClass(6m).Should().Be("text-amber-600");
        DashboardViewHelper.RunwayCssClass(4.5m).Should().Be("text-amber-600");
    }

    [Fact]
    public void RunwayCssClass_ReturnsRed_WhenRunwayIsBelowThreeMonths()
    {
        DashboardViewHelper.RunwayCssClass(2.9m).Should().Be("text-red-600");
        DashboardViewHelper.RunwayCssClass(0m).Should().Be("text-red-600");
    }
}
