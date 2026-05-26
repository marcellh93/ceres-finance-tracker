// ProjectCeres/ViewModels/CsvImportProfileViewModel.cs
using ProjectCeres.Analyzers.Annotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class ImportProfileViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ImportColumnMappings Mappings { get; set; } = new();
    public ImportFormat Format { get; set; }
    public string? SheetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    [AllowsWallClock("view-model computed property; DTOs aren't DI-resolved")]
    public int DaysUntilPurge =>
        DeletedAt.HasValue
            ? Math.Max(0, 90 - (int)(DateTime.UtcNow - DeletedAt.Value).TotalDays)
            : 0;
}
