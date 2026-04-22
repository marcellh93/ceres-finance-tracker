namespace ProjectCeres.Models;

public class CsvImportProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColumnMappings { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
