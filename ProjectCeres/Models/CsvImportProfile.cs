// ProjectCeres/Models/CsvImportProfile.cs
namespace ProjectCeres.Models;

public class ImportProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMappings { get; set; } = "{}";
    public ImportFormat Format { get; set; } = ImportFormat.Csv;
    public string? SheetName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
