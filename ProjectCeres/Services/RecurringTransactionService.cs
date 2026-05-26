using Microsoft.EntityFrameworkCore;
using ProjectCeres.Common;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class RecurringTransactionService(AppDbContext db, IAccountService accountService, ICurrentUserAccessor user, TimeProvider timeProvider) : IRecurringTransactionService
{
    public async Task<IEnumerable<RecurringTransaction>> GetAllAsync(bool includeInactive = false)
    {
        var query = db.RecurringTransactions
            .Owned(user)
            .Include(r => r.Account)
            .Include(r => r.Category)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        return await query.OrderBy(r => r.NextDueDate).ToListAsync();
    }

    public async Task<RecurringTransaction?> GetByIdAsync(Guid id) =>
        await db.RecurringTransactions
            .Owned(user)
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<IEnumerable<RecurringTransaction>> GetUpcomingAsync(int withinDays)
    {
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var cutoff = today.AddDays(withinDays);

        return await db.RecurringTransactions
            .Owned(user)
            .Include(r => r.Account)
            .Include(r => r.Category)
            .Where(r => r.IsActive && r.NextDueDate <= cutoff)
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
        if (reminder.DayOfPeriod is null)
            return AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency);

        return reminder.Frequency switch
        {
            Frequency.Monthly  => SnapMonthly(reminder, confirmDate),
            Frequency.Weekly   => SnapWeekly(reminder, confirmDate, doubleStep: false),
            Frequency.Biweekly => SnapWeekly(reminder, confirmDate, doubleStep: true),
            _                  => AdvanceByFrequency(reminder.NextDueDate, reminder.Frequency),
        };
    }

    private static DateOnly SnapMonthly(RecurringTransaction reminder, DateOnly? confirmDate)
    {
        var day  = reminder.DayOfPeriod!.Value;
        var from = confirmDate ?? reminder.NextDueDate;

        var candidate = new DateOnly(from.Year, from.Month, 1).AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(candidate.Year, candidate.Month);
        return new DateOnly(candidate.Year, candidate.Month, Math.Min(day, daysInMonth));
    }

    private static DateOnly SnapWeekly(RecurringTransaction reminder, DateOnly? confirmDate, bool doubleStep)
    {
        // DayOfPeriod: 1=Monday … 7=Sunday (ISO 8601)
        // DayOfWeek:   Sunday=0 … Saturday=6
        var targetDow = (DayOfWeek)(reminder.DayOfPeriod!.Value % 7);
        var from = confirmDate ?? reminder.NextDueDate;
        var daysAhead = ((int)targetDow - (int)from.DayOfWeek + 7) % 7;
        if (daysAhead == 0) daysAhead = 7;           // never return same-day
        if (doubleStep && daysAhead < 8) daysAhead += 7;
        return from.AddDays(daysAhead);
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

    // -------------------------------------------------------------------------
    // API surface (Result-returning).
    // -------------------------------------------------------------------------

    public async Task<Result<RecurringTransaction>> TryCreateAsync(CreateRecurringTransactionRequest request)
    {
        if (!Enum.TryParse<Frequency>(request.Frequency, out var freq))
            return Result<RecurringTransaction>.Fail("INVALID_FREQUENCY", $"Unknown frequency '{request.Frequency}'.");
        if (!Enum.TryParse<ReminderBehaviour>(request.ReminderBehaviour, out var behaviour))
            return Result<RecurringTransaction>.Fail("INVALID_REMINDER_BEHAVIOUR", $"Unknown reminder behaviour '{request.ReminderBehaviour}'.");

        var scheduleCheck = RecurringTransactionPolicies.ValidateSchedule(freq, behaviour, request.DayOfPeriod);
        if (!scheduleCheck.IsSuccess)
        {
            var err = scheduleCheck.Error!.Value;
            return Result<RecurringTransaction>.Fail(err.Code, err.Message);
        }

        var accountOk = await db.Accounts.Owned(user).AnyAsync(a => a.Id == request.AccountId!.Value);
        if (!accountOk)
            return Result<RecurringTransaction>.Fail("INVALID_ACCOUNT", "The selected account does not exist.");

        var categoryOk = await db.Categories.Owned(user).AnyAsync(c => c.Id == request.CategoryId!.Value);
        if (!categoryOk)
            return Result<RecurringTransaction>.Fail("INVALID_CATEGORY", "The selected category does not exist.");

        var reminder = new RecurringTransaction
        {
            Id                = Guid.NewGuid(),
            UserId            = user.UserId,
            Name              = request.Name.Trim(),
            EstimatedAmount   = request.EstimatedAmount,
            AccountId         = request.AccountId!.Value,
            CategoryId        = request.CategoryId!.Value,
            Frequency         = freq,
            DayOfPeriod       = request.DayOfPeriod,
            NextDueDate       = request.NextDueDate,
            IsActive          = true,
            ReminderBehaviour = behaviour
        };

        db.RecurringTransactions.Add(reminder);
        await db.SaveChangesAsync();

        var fresh = await db.RecurringTransactions
            .Owned(user)
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstAsync(r => r.Id == reminder.Id);
        return Result<RecurringTransaction>.Ok(fresh);
    }

    public async Task<Result<RecurringTransaction>> TryUpdateAsync(Guid id, UpdateRecurringTransactionRequest request)
    {
        if (!Enum.TryParse<Frequency>(request.Frequency, out var freq))
            return Result<RecurringTransaction>.Fail("INVALID_FREQUENCY", $"Unknown frequency '{request.Frequency}'.");
        if (!Enum.TryParse<ReminderBehaviour>(request.ReminderBehaviour, out var behaviour))
            return Result<RecurringTransaction>.Fail("INVALID_REMINDER_BEHAVIOUR", $"Unknown reminder behaviour '{request.ReminderBehaviour}'.");

        var scheduleCheck = RecurringTransactionPolicies.ValidateSchedule(freq, behaviour, request.DayOfPeriod);
        if (!scheduleCheck.IsSuccess)
        {
            var err = scheduleCheck.Error!.Value;
            return Result<RecurringTransaction>.Fail(err.Code, err.Message);
        }

        var reminder = await db.RecurringTransactions.Owned(user)
            .Include(r => r.Account).Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null)
            return Result<RecurringTransaction>.Fail("NOT_FOUND", "Recurring transaction not found.");

        var accountOk = await db.Accounts.Owned(user).AnyAsync(a => a.Id == request.AccountId!.Value);
        if (!accountOk)
            return Result<RecurringTransaction>.Fail("INVALID_ACCOUNT", "The selected account does not exist.");

        var categoryOk = await db.Categories.Owned(user).AnyAsync(c => c.Id == request.CategoryId!.Value);
        if (!categoryOk)
            return Result<RecurringTransaction>.Fail("INVALID_CATEGORY", "The selected category does not exist.");

        reminder.Name              = request.Name.Trim();
        reminder.EstimatedAmount   = request.EstimatedAmount;
        reminder.AccountId         = request.AccountId!.Value;
        reminder.CategoryId        = request.CategoryId!.Value;
        reminder.Frequency         = freq;
        reminder.DayOfPeriod       = request.DayOfPeriod;
        reminder.NextDueDate       = request.NextDueDate;
        reminder.ReminderBehaviour = behaviour;
        await db.SaveChangesAsync();
        return Result<RecurringTransaction>.Ok(reminder);
    }

    public async Task<Result> TryDeactivateAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null) return Result.Fail("NOT_FOUND", "Recurring transaction not found.");
        reminder.IsActive = false;
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result<Transaction>> TryConfirmAsync(Guid id, ConfirmRecurringTransactionRequest request)
    {
        var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null) return Result<Transaction>.Fail("NOT_FOUND", "Recurring transaction not found.");

        var openingDate = await accountService.GetOpeningBalanceDateAsync(reminder.AccountId);
        if (openingDate.HasValue && request.Date < openingDate.Value)
            return Result<Transaction>.Fail("DATE_BEFORE_OPENING_BALANCE",
                $"This transaction cannot be dated before the opening balance date ({openingDate.Value:dd/MM/yyyy}).");

        // ManualDate behaviour requires an explicit nextDueDate; surface that as a 422 instead of an exception.
        if (reminder.ReminderBehaviour == ReminderBehaviour.ManualDate && request.NextDueDate is null)
            return Result<Transaction>.Fail("NEXT_DUE_DATE_REQUIRED",
                "Please set the next due date before confirming a ManualDate reminder.");

        var transaction = new Transaction
        {
            Id          = Guid.NewGuid(),
            UserId      = user.UserId,
            Date        = request.Date,
            Amount      = request.Amount,
            Description = request.Description ?? reminder.Name,
            AccountId   = reminder.AccountId,
            CategoryId  = reminder.CategoryId,
            CreatedAt   = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Transactions.Add(transaction);
        reminder.NextDueDate = AdvanceDueDate(reminder, confirmDate: request.Date, nextDueDate: request.NextDueDate);
        await db.SaveChangesAsync();
        return Result<Transaction>.Ok(transaction);
    }

    public async Task<Result> TryDismissAsync(Guid id, DateOnly? nextDueDate)
    {
        var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null) return Result.Fail("NOT_FOUND", "Recurring transaction not found.");

        // ManualDate behaviour requires an explicit nextDueDate; surface that as a 422 instead of an exception.
        if (reminder.ReminderBehaviour == ReminderBehaviour.ManualDate && nextDueDate is null)
            return Result.Fail("NEXT_DUE_DATE_REQUIRED",
                "Please set the next due date before dismissing a ManualDate reminder.");

        reminder.NextDueDate = AdvanceDueDate(reminder, confirmDate: null, nextDueDate: nextDueDate);
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> TryReactivateAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.Owned(user).FirstOrDefaultAsync(r => r.Id == id);
        if (reminder is null) return Result.Fail("NOT_FOUND", "Recurring transaction not found.");
        if (reminder.IsActive) return Result.Ok();
        reminder.IsActive = true;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
