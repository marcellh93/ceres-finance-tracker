using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record ImportProfileListItemDto(
    Guid                  Id,
    string                Name,
    string                Format,
    string?               SheetName,
    ImportColumnMappings  Mappings,
    DateTime              CreatedAt,
    DateTime?             DeletedAt,
    int                   DaysUntilPurge);

public record CreateImportProfileRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] string Format,
    [Required] ImportColumnMappings Mappings);

public record UpdateImportProfileRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] ImportColumnMappings Mappings);
