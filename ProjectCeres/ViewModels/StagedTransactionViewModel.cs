namespace ProjectCeres.ViewModels;

public class StagedTransactionViewModel
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly RawDate { get; set; }
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public string? MatchedTransactionDescription { get; set; }
    public DateOnly MatchedTransactionDate { get; set; }
    public decimal MatchedTransactionAmount { get; set; }
}
