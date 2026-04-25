namespace ProjectCeres.ViewModels;

public class CsvImportProfileViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CsvColumnMappings Mappings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public int DaysUntilPurge =>
        DeletedAt.HasValue
            ? Math.Max(0, 90 - (int)(DateTime.UtcNow - DeletedAt.Value).TotalDays)
            : 0;
}
