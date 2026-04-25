namespace ProjectCeres.ViewModels;

public class ParsedImportRow
{
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}
