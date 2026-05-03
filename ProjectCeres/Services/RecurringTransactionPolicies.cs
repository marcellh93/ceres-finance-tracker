using ProjectCeres.Common;
using ProjectCeres.Models;

namespace ProjectCeres.Services;

public static class RecurringTransactionPolicies
{
    public const string InvalidDayOfPeriodCode = "INVALID_DAY_OF_PERIOD";

    public static Result ValidateSchedule(Frequency frequency, ReminderBehaviour behaviour, int? dayOfPeriod)
    {
        var snapNonAnnual = behaviour == ReminderBehaviour.SnapToCalendarDay
                         && frequency != Frequency.Annual;

        if (!snapNonAnnual && dayOfPeriod is not null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period applies only to Snap-to-calendar-day reminders that are not Annual.");

        if (snapNonAnnual && dayOfPeriod is null)
            return Result.Fail(InvalidDayOfPeriodCode,
                "Day of period is required for Snap-to-calendar-day reminders.");

        if (snapNonAnnual)
        {
            var max = frequency switch
            {
                Frequency.Monthly => 31,
                _                 => 7,   // Weekly + Biweekly: ISO 8601 day-of-week 1–7
            };
            if (dayOfPeriod < 1 || dayOfPeriod > max)
                return Result.Fail(InvalidDayOfPeriodCode,
                    $"Day of period must be 1–{max} for {frequency} reminders.");
        }

        return Result.Ok();
    }
}
