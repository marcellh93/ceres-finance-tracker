namespace ProjectCeres.ViewModels;

public class ImportSummaryViewModel
{
    public int RowsImported   { get; set; }
    public int RowsReconciled { get; set; }
    public int RowsFlagged    { get; set; }
    public int RowsStaged     { get; set; }
    public int RowsFailed     { get; set; }
    public List<string> Errors { get; set; } = [];

    // Set when import used manual column mappings (no saved profile) — offer to save
    public ImportColumnMappings? MappingsToSave { get; set; }

    public int TotalProcessed => RowsImported + RowsReconciled + RowsFlagged + RowsStaged + RowsFailed;
}
