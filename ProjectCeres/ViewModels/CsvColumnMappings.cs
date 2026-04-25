// ProjectCeres/ViewModels/CsvColumnMappings.cs
namespace ProjectCeres.ViewModels;

public class ImportColumnMappings
{
    public string DateColumn { get; set; } = string.Empty;
    public string AmountColumn { get; set; } = string.Empty;
    public string DescriptionColumn { get; set; } = string.Empty;
    public string? CategoryColumn { get; set; }
    /// <summary>When true, negate debit values to positive amounts on import.</summary>
    public bool FlipDebitSign { get; set; }
    /// <summary>Excel only. If set, read this worksheet by name. If null, read the first worksheet.</summary>
    public string? SheetName { get; set; }
}
