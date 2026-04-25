using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface ICsvImportProfileService
{
    Task<IEnumerable<CsvImportProfileViewModel>> GetAllActiveAsync();
    Task<IEnumerable<CsvImportProfileViewModel>> GetRecentlyDeletedAsync();
    Task<CsvImportProfileViewModel?> GetByIdAsync(Guid id);
    Task<Guid> CreateAsync(string name, CsvColumnMappings mappings);
    Task UpdateAsync(Guid id, string name, CsvColumnMappings mappings);
    Task DeleteAsync(Guid id);
    Task RecoverAsync(Guid id);
}
