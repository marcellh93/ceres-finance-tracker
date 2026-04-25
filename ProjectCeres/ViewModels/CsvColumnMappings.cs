namespace ProjectCeres.ViewModels;

public class CsvColumnMappings
{
    public string DateColumn { get; set; } = string.Empty;
    public string AmountColumn { get; set; } = string.Empty;
    public string DescriptionColumn { get; set; } = string.Empty;
    public string? CategoryColumn { get; set; }
    /// <summary>When true, negate debit values to positive amounts on import.</summary>
    public bool FlipDebitSign { get; set; }
}
