namespace ProjectCeres.ViewModels;

public class ImportResult
{
    public int RowsImported    { get; set; }
    public int RowsReconciled  { get; set; }
    public int RowsFlagged     { get; set; }
    public int RowsFailed      { get; set; }
    public List<string> Errors { get; set; } = [];
}
