using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IRecurringTransactionService
{
    Task<IEnumerable<RecurringTransaction>> GetAllAsync(bool includeInactive = false);
    Task<RecurringTransaction?> GetByIdAsync(Guid id);
    Task<IEnumerable<RecurringTransaction>> GetUpcomingAsync(int withinDays);

    // API surface (Result-returning).
    Task<Result<RecurringTransaction>> TryCreateAsync(CreateRecurringTransactionRequest request);
    Task<Result<RecurringTransaction>> TryUpdateAsync(Guid id, UpdateRecurringTransactionRequest request);
    Task<Result> TryDeactivateAsync(Guid id);
    Task<Result<Transaction>> TryConfirmAsync(Guid id, ConfirmRecurringTransactionRequest request);
    Task<Result> TryDismissAsync(Guid id, DateOnly? nextDueDate);
    Task<Result> TryReactivateAsync(Guid id);
}
