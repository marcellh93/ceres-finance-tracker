using Microsoft.AspNetCore.Http;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public interface IImportService
{
    Task<IReadOnlyList<ParsedImportRow>> ParseAsync(IFormFile file, CsvColumnMappings mappings);
    string GenerateFingerprint(DateOnly date, decimal amount, string? description, Guid accountId);
    Task<ImportResult> ImportAsync(IFormFile file, Guid accountId, Guid categoryId, CsvColumnMappings mappings);
}
