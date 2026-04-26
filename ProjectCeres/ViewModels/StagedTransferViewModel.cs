using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class StagedTransferViewModel
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public DateOnly RawDate { get; set; }
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid? CandidateTransactionId { get; set; }
    public string? CandidateTransactionDescription { get; set; }
    public DateOnly? CandidateTransactionDate { get; set; }
    public decimal? CandidateTransactionAmount { get; set; }
}
