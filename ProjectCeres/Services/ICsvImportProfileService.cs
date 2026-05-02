// ProjectCeres/Services/ICsvImportProfileService.cs
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportProfileService
{
    // Razor-era methods (throwing).
    Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync();
    Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync();
    Task<ImportProfileViewModel?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(string name, ImportFormat format, ImportColumnMappings mappings);
    Task UpdateAsync(Guid id, string name, ImportColumnMappings mappings);
    Task DeleteAsync(Guid id);
    Task RecoverAsync(Guid id);

    // API surface (Result-returning).
    Task<Result<Guid>> TryCreateAsync(string name, ImportFormat format, ImportColumnMappings mappings);
    Task<Result> TryUpdateAsync(Guid id, string name, ImportColumnMappings mappings);
    Task<Result> TryDeleteAsync(Guid id);
    Task<Result> TryRecoverAsync(Guid id);
}
