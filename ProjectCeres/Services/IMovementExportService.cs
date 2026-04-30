using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IMovementExportService
{
    Task<string> BuildCsvAsync(
        Guid? accountId,
        DateOnly? from,
        DateOnly? to,
        string? q,
        MovementType? type);
}
