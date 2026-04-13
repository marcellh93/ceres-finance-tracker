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
            Id              = Guid.NewGuid(),
            Name            = vm.Name,
            EstimatedAmount = vm.EstimatedAmount,
            AccountId       = vm.AccountId!.Value,
            CategoryId      = vm.CategoryId!.Value,
            Frequency       = vm.Frequency,
            DayOfPeriod     = vm.DayOfPeriod,
            NextDueDate     = vm.NextDueDate,
            IsActive        = true
        };

        db.RecurringTransactions.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    public async Task UpdateAsync(RecurringTransactionEditViewModel vm)
    {
        var reminder = await db.RecurringTransactions.FindAsync(vm.Id)
            ?? throw new InvalidOperationException($"Recurring transaction {vm.Id} not found.");

        reminder.Name            = vm.Name;
        reminder.EstimatedAmount = vm.EstimatedAmount;
        reminder.AccountId       = vm.AccountId!.Value;
        reminder.CategoryId      = vm.CategoryId!.Value;
        reminder.Frequency       = vm.Frequency;
        reminder.DayOfPeriod     = vm.DayOfPeriod;
        reminder.NextDueDate     = vm.NextDueDate;
        await db.SaveChangesAsync();
    }

    public async Task DeactivateAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Recurring transaction {id} not found.");

        reminder.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<Transaction> ConfirmAsync(Guid id, DateOnly date, decimal amount, string? description)
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
        reminder.NextDueDate = AdvanceDueDate(reminder);
        await db.SaveChangesAsync();
        return transaction;
    }

    public async Task DismissAsync(Guid id)
    {
        var reminder = await db.RecurringTransactions.FindAsync(id)
            ?? throw new InvalidOperationException($"Recurring transaction {id} not found.");

        reminder.NextDueDate = AdvanceDueDate(reminder);
        await db.SaveChangesAsync();
    }

    private static DateOnly AdvanceDueDate(RecurringTransaction reminder) =>
        reminder.Frequency switch
        {
            Frequency.Weekly    => reminder.NextDueDate.AddDays(7),
            Frequency.Biweekly  => reminder.NextDueDate.AddDays(14),
            Frequency.Monthly   => reminder.NextDueDate.AddMonths(1),
            Frequency.Annual    => reminder.NextDueDate.AddYears(1),
            _                   => reminder.NextDueDate.AddMonths(1)
        };
}
