using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ITransferService
{
    Task<IEnumerable<Transfer>> GetAllAsync();
    Task<Transfer?> GetByIdAsync(Guid id);
    /// <summary>Creates transfer. Throws if source and destination accounts have different currencies.</summary>
    Task<Transfer> CreateAsync(TransferCreateViewModel vm);
    Task UpdateAsync(TransferEditViewModel vm);
    /// <summary>Hard delete with no soft-delete fallback.</summary>
    Task DeleteAsync(Guid id);
    Task MarkClearedAsync(Guid id, bool cleared);
    Task<int> BulkMarkClearedAsync(DateOnly from, DateOnly to, Guid? accountId = null);
}
