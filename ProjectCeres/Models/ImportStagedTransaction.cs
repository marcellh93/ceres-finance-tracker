using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectCeres.Models;

public class ImportStagedTransaction
{
    public Guid Id { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly RawDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid MatchedTransactionId { get; set; }
    public StagedTransactionStatus Status { get; set; } = StagedTransactionStatus.Pending;
    public DateTime? ResolvedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Transaction MatchedTransaction { get; set; } = null!;
}
