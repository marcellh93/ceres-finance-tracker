// ProjectCeres/Models/CsvImportProfile.cs
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class ImportProfile : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMappings { get; set; } = "{}";
    public ImportFormat Format { get; set; } = ImportFormat.Csv;
    public string? SheetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
