using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IMovementService
{
    Task<List<MovementListItemViewModel>> GetRecentAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int limit = 50,
        int offset = 0,
        string? q = null,
        MovementType? type = null);

    Task<int> CountAsync(
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? q = null,
        MovementType? type = null);

    Task<MovementType?> GetTypeAsync(Guid id);
}
