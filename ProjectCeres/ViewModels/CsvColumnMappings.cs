// ProjectCeres/ViewModels/CsvColumnMappings.cs
namespace ProjectCeres.ViewModels;

public class ImportColumnMappings
{
    public string DateColumn { get; set; } = string.Empty;
    public string AmountColumn { get; set; } = string.Empty;
    public string DescriptionColumn { get; set; } = string.Empty;
    public string? CategoryColumn { get; set; }
    /// <summary>
    /// Retained for back-compat with import profiles. The parsers no longer act on this
    /// flag — direction is inferred from the raw signed amount in ImportService. Default
    /// `true` reflects the standard bank convention (negative for debits).
    /// </summary>
    public bool FlipDebitSign { get; set; } = true;
    /// <summary>Excel only. If set, read this worksheet by name. If null, read the first worksheet.</summary>
    public string? SheetName { get; set; }
}
