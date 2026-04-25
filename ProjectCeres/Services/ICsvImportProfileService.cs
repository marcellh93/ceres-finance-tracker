// ProjectCeres/Services/ICsvImportProfileService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportProfileService
{
    Task<IEnumerable<ImportProfileViewModel>> GetAllActiveAsync();
    Task<IEnumerable<ImportProfileViewModel>> GetRecentlyDeletedAsync();
    Task<ImportProfileViewModel?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(string name, ImportFormat format, ImportColumnMappings mappings);
    Task UpdateAsync(Guid id, string name, ImportColumnMappings mappings);
    Task DeleteAsync(Guid id);
    Task RecoverAsync(Guid id);
}
