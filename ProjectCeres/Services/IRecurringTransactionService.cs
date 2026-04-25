using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IRecurringTransactionService
{
    Task<IEnumerable<RecurringTransaction>> GetAllAsync(bool includeInactive = false);
    Task<RecurringTransaction?> GetByIdAsync(Guid id);
    Task<RecurringTransaction> CreateAsync(RecurringTransactionCreateViewModel vm);
    Task UpdateAsync(RecurringTransactionEditViewModel vm);
    Task DeactivateAsync(Guid id);
    /// <summary>Creates a transaction from the template and advances NextDueDate per ReminderBehaviour.</summary>
    Task<Transaction> ConfirmAsync(Guid id, DateOnly date, decimal amount, string? description, DateOnly? nextDueDate = null);
    /// <summary>Advances NextDueDate without creating a transaction.</summary>
    Task DismissAsync(Guid id);
    /// <summary>Returns active reminders with NextDueDate within the given number of days from today.</summary>
    Task<IEnumerable<RecurringTransaction>> GetUpcomingAsync(int withinDays);
}
