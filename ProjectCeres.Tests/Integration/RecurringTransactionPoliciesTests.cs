using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Integration;

public class RecurringTransactionPoliciesTests
{
    [Fact]
    public void ValidateSchedule_SnapMonthly_WithDay15_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapMonthly_WithDay32_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 32);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapWeekly_WithDay4_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 4);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapWeekly_WithDay8_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Weekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 8);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_Manual_WithDay15_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.ManualDate, dayOfPeriod: 15);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapAnnual_WithDay15_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Annual, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 15);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_SnapMonthly_WithNullDay_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: null);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }

    [Fact]
    public void ValidateSchedule_Manual_WithNullDay_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Monthly, ReminderBehaviour.ManualDate, dayOfPeriod: null);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapBiweekly_WithDay1_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 1);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapBiweekly_WithDay7_Succeeds()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 7);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateSchedule_SnapBiweekly_WithDay8_Fails()
    {
        var result = RecurringTransactionPolicies.ValidateSchedule(
            Frequency.Biweekly, ReminderBehaviour.SnapToCalendarDay, dayOfPeriod: 8);
        result.IsSuccess.Should().BeFalse();
        result.Error!.Value.Code.Should().Be(RecurringTransactionPolicies.InvalidDayOfPeriodCode);
    }
}
