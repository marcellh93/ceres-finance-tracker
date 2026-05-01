using FluentAssertions;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class BudgetPeriodTests
{
    [Theory]
    // (year, month, startDay, expectedStart, expectedEnd)
    [InlineData(2026, 5,  1, "2026-05-01", "2026-05-31")] // Default — calendar month
    [InlineData(2026, 5, 25, "2026-04-25", "2026-05-24")] // Mid-month start, mid-period
    [InlineData(2026, 4, 31, "2026-03-31", "2026-04-29")] // Start=31, April fallback to 30 → end=29
    [InlineData(2026, 5, 31, "2026-04-30", "2026-05-30")] // Start=31, May has 31 → end=30
    [InlineData(2026, 3, 31, "2026-02-28", "2026-03-30")] // Feb fallback (non-leap) → start=Feb 28
    [InlineData(2024, 3, 31, "2024-02-29", "2024-03-30")] // Feb fallback (leap) → start=Feb 29
    [InlineData(2026, 2, 30, "2026-01-30", "2026-02-27")] // Start=30, Feb fallback → end=Feb 27 (Feb 28 - 1)
    [InlineData(2024, 2, 30, "2024-01-30", "2024-02-28")] // Start=30, leap Feb → end=Feb 28 (Feb 29 - 1)
    [InlineData(2026, 6,  1, "2026-06-01", "2026-06-30")] // June calendar
    [InlineData(2026, 1,  1, "2026-01-01", "2026-01-31")] // January calendar
    public void GetBoundsForMonth_returns_expected_range(
        int year, int month, int startDay, string expectedStart, string expectedEnd)
    {
        var (start, end) = BudgetPeriod.GetBoundsForMonth(year, month, startDay);

        start.Should().Be(DateOnly.Parse(expectedStart));
        end.Should().Be(DateOnly.Parse(expectedEnd));
    }

    [Theory]
    // (today, startDay, expectedYear, expectedMonth)
    [InlineData("2026-05-01",  1, 2026, 5)]
    [InlineData("2026-05-01", 25, 2026, 5)] // Apr 25 – May 24 → period name May
    [InlineData("2026-04-24", 25, 2026, 4)] // Mar 25 – Apr 24 → period name April
    [InlineData("2026-04-25", 25, 2026, 5)] // First day of new period → name flips to May
    [InlineData("2026-04-10", 31, 2026, 4)] // Today between Mar 31 (start) and Apr 29 (end) → April
    [InlineData("2026-05-10", 31, 2026, 5)] // Today between Apr 30 (start) and May 30 (end) → May
    [InlineData("2026-12-31",  1, 2026, 12)]
    public void GetCurrentPeriodMonth_returns_expected_period_name(
        string today, int startDay, int expectedYear, int expectedMonth)
    {
        var (year, month) = BudgetPeriod.GetCurrentPeriodMonth(DateOnly.Parse(today), startDay);

        year.Should().Be(expectedYear);
        month.Should().Be(expectedMonth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-5)]
    public void GetBoundsForMonth_rejects_invalid_start_day(int startDay)
    {
        Action act = () => BudgetPeriod.GetBoundsForMonth(2026, 5, startDay);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
