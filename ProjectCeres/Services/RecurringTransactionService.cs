using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class RecurringTransactionService(AppDbContext db, IAccountService accountService) : IRecurringTransactionService
{
    public async Task<IEnumerable<RecurringTransaction>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        return await query.OrderBy(r => r.NextDueDate).ToListAsync();
    }

    public async Task<RecurringTransaction?> GetByIdAsync(Guid id) =>
        await db.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<RecurringTransaction> CreateAsync(RecurringTransactionCreateViewModel vm)
    {
        var reminder = new RecurringTransaction
        {
            Id                = Guid.NewGuid(),
            Name              = vm.Name,
            EstimatedAmount   = vm.EstimatedAmount,
            AccountId         = vm.AccountId!.Value,
            CategoryId        = vm.CategoryId!.Value,
            Frequency         = vm.Frequency,
            DayOfPeriod       = vm.DayOfPeriod,
            NextDueDate       = vm.NextDueDate,
            IsActive          = true,
            ReminderBehaviour = vm.ReminderBehaviour
        };

        db.RecurringTransactions.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    public async Task UpdateAsync(RecurringTransactionEditViewModel vm)
    {
        var reminder = await db.RecurringTransactions.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Recurring transaction {vm.Id} not found.");

        reminder.Name              = vm.Name;
        reminder.EstimatedAmount   = vm.EstimatedAmount;
        reminder.AccountId         = vm.AccountId!.Value;
        reminder.CategoryId        = vm.CategoryId!.Value;
        reminder.Frequency         = vm.Frequency;
        reminder.DayOfPeriod       = vm.DayOfPeriod;
        reminder.NextDueDate       = vm.NextDueDate;
        reminder.ReminderBehaviour = vm.ReminderBehaviour;
        await db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Recurring transaction {id} not found.");

        reminder.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<Transaction> ConfirmAsync(Guid id, DateOnly date, decimal amount, string? description, DateOnly? nextDueDate = null)
    {
        var reminder = await db.RecurringTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Recurring transaction {id} not found.");

        var openingDate = await accountService.GetOpeningBalanceDateAsync(reminder.AccountId);
        if (openingDate.HasValue && date < openingDate.Value)
            throw new InvalidOperationException(
                $"This transaction cannot be dated before the opening balance date ({openingDate.Value:dd/MM/yyyy}). " +
                $"To allow earlier dates, edit the account and move the opening balance date to {date:dd/MM/yyyy} or earlier.");

        var transaction = new Transaction
        {
            Id          = Guid.NewGuid(),
            Date        = date,
            Amount      = amount,
            Description = description ?? reminder.Name,
            AccountId   = reminder.AccountId,
            CategoryId  = reminder.CategoryId,
            CreatedAt   = DateTime.UtcNow
        };

        db.Transactions.Add(transaction);
        reminder.NextDueDate = AdvanceDueDate(reminder, confirmDate: date, nextDueDate: nextDueDate);
        await db.SaveChangesAsync();
        return transaction;
    }

    public async Task DismissAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Recurring transaction {id} not found.");

        reminder.NextDueDate = AdvanceDueDate(reminder, confirmDate: null, nextDueDate: null);
        await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<RecurringTransaction>> GetUpcomingAsync(int withinDays)
    {
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var cutoff = today.AddDays(withinDays);

        return await db.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .Where(r => r.IsActive && r.NextDueDate >= today && r.NextDueDate <= cutoff)
            .OrderBy(r => r.NextDueDate)
            .ToListAsync();
    }

    private static DateOnly AdvanceDueDate(RecurringTransaction reminder, DateOnly? confirmDate, DateOnly? nextDueDate)
    {
        return reminder.ReminderBehaviour switch
        {
            ReminderBehaviour.ManualDate => nextDueDate
                ?? throw new InvalidOperationException("Please set the next due date before confirming a ManualDate reminder."),

            ReminderBehaviour.RelativeToLastConfirmation => confirmDate.HasValue
                ? AdvanceByFrequency(confirmDate.Value, reminder.Frequency)
                : AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency),

            // Default: SnapToCalendarDay — snap to DayOfPeriod; if already past that day this month, skip forward
            _ => SnapToCalendarDay(reminder, confirmDate)
        };
    }

    private static DateOnly SnapToCalendarDay(RecurringTransaction reminder, DateOnly? confirmDate)
    {
        if (reminder.DayOfPeriod is null || reminder.Frequency != Frequency.Monthly)
            return AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency);

        var day  = reminder.DayOfPeriod.Value;
        var from = confirmDate ?? reminder.NextDueDate;

        // Try the target day in the month after the confirm date
        var candidate = new DateOnly(from.Year, from.Month, 1).AddMonths(1);
        // Clamp day to last day of that month
        var daysInMonth = DateTime.DaysInMonth(candidate.Year, candidate.Month);
        candidate = new DateOnly(candidate.Year, candidate.Month, Math.Min(day, daysInMonth));

        // If we confirmed on or after DayOfPeriod, the next occurrence is already within the same
        // +1 month window, but if DayOfPeriod has already passed in that +1 month relative to
        // confirmDate, skip one more month.
        if (from.Day >= day)
            candidate = candidate.AddMonths(1);

        return candidate;
    }

    private static DateOnly AdvanceByFrequency(DateOnly from, Frequency frequency) =>
        frequency switch
        {
            Frequency.Weekly    => from.AddDays(7),
            Frequency.Biweekly  => from.AddDays(14),
            Frequency.Monthly   => from.AddMonths(1),
            Frequency.Annual    => from.AddYears(1),
            _                   => from.AddMonths(1)
        };
}
