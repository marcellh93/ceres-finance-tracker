using System.ComponentModel.DataAnnotations.Schema;
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public class ImportStagedTransfer : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime ImportedAt { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly RawDate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal RawAmount { get; set; }
    public string? RawDescription { get; set; }
    public Guid? CandidateTransactionId { get; set; }
    public StagedTransferStatus Status { get; set; } = StagedTransferStatus.Pending;
    public DateTime? ResolvedAt { get; set; }

    public Account Account { get; set; } = null!;
    public Transaction? CandidateTransaction { get; set; }
}
