namespace ProjectCeres.ViewModels;

public class ImportSummaryViewModel
{
    public int RowsImported { get; set; }
    public int RowsFlagged { get; set; }
    public int RowsFailed { get; set; }
    public List<string> Errors { get; set; } = [];

    public int TotalProcessed => RowsImported + RowsFlagged + RowsFailed;
}
