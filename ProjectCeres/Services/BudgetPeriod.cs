namespace ProjectCeres.Services;

/// <summary>
/// Pure helper for budget-period boundary math driven by
/// <c>Settings.BudgetPeriodStartDay</c>. See spec §5.2 / §5.3 for the rule:
/// for shorter months, the start day falls back to the month's last day;
/// the period whose end falls in (year, month) is named after that month.
/// </summary>
public static class BudgetPeriod
{
    /// <summary>
    /// Given a target period-name (year, month) and the configured start day (1–31),
    /// returns the [start, end] DateOnly range of that period.
    /// For startDay=1, returns a calendar month. For startDay>1, the period spans
    /// two calendar months, ending the day before that month's start day.
    /// </summary>
    public static (DateOnly Start, DateOnly End) GetBoundsForMonth(int year, int month, int startDay)
    {
        if (startDay < 1 || startDay > 31)
            throw new ArgumentOutOfRangeException(nameof(startDay), "Start day must be 1–31.");

        DateOnly start;
        DateOnly end;

        if (startDay == 1)
        {
            // Calendar month: start = 1st, end = last day of month
            start = new DateOnly(year, month, 1);
            end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        }
        else
        {
            // Period spans two months: starts on day startDay of previous month,
            // ends the day before day startDay of this month.
            var thisMonthStartDate = ApplyStartDayInMonth(year, month, startDay);
            end = thisMonthStartDate.AddDays(-1);

            // Get the previous month
            var (prevYear, prevMonth) = month > 1
                ? (year, month - 1)
                : (year - 1, 12);

            start = ApplyStartDayInMonth(prevYear, prevMonth, startDay);
        }

        return (start, end);
    }

    /// <summary>
    /// Given today and the configured start day, returns (year, month) of the
    /// CURRENT period — the period whose end falls in that calendar month.
    /// </summary>
    public static (int Year, int Month) GetCurrentPeriodMonth(DateOnly today, int startDay)
    {
        if (startDay < 1 || startDay > 31)
            throw new ArgumentOutOfRangeException(nameof(startDay), "Start day must be 1–31.");

        // For startDay=1 (calendar month), the period always matches the calendar month.
        if (startDay == 1)
            return (today.Year, today.Month);

        // For startDay>1: today is either before or on/after this calendar month's start day.
        // If before: period ends in this calendar month.
        // If on/after: period ends in the next calendar month.
        var thisMonthStart = ApplyStartDayInMonth(today.Year, today.Month, startDay);

        if (today < thisMonthStart)
            return (today.Year, today.Month);

        var next = today.AddMonths(1);
        return (next.Year, next.Month);
    }

    /// <summary>
    /// Returns DateOnly(year, month, min(startDay, lastDayOfMonth)).
    /// </summary>
    private static DateOnly ApplyStartDayInMonth(int year, int month, int startDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var actualDay   = Math.Min(startDay, daysInMonth);
        return new DateOnly(year, month, actualDay);
    }
}
