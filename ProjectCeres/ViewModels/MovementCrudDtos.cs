// ProjectCeres/ViewModels/MovementCrudDtos.cs
using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

// ---------- Read DTOs (responses for GET /:id) ----------

public record TransactionEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid AccountId,
    Guid CategoryId,
    string? Description,
    bool IsCleared,
    IReadOnlyList<AttachmentDto> Attachments);

public record TransferEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid SourceAccountId,
    Guid DestAccountId,
    string? Description,
    bool IsCleared,
    IReadOnlyList<AttachmentDto> Attachments);

public record LiabilityPaymentEditDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    Guid AssetAccountId,
    Guid LiabilityAccountId,
    string? Description,
    bool IsCleared);

public record AttachmentDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    DateTime UploadedAt);

public record MovementTypeDto(Guid Id, string MovementType);

// ---------- Write DTOs (request bodies for PUT /:id) ----------

public class UpdateTransactionRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public Guid? CategoryId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }

    public Guid? BudgetId { get; set; }

    public bool NeedsReview { get; set; }
}

public class UpdateTransferRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select a source account.")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Please select a destination account.")]
    public Guid? DestAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }
}

public class UpdateLiabilityPaymentRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an asset account.")]
    public Guid? AssetAccountId { get; set; }

    [Required(ErrorMessage = "Please select a liability account.")]
    public Guid? LiabilityAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    public bool IsCleared { get; set; }
}

// ---------- Bulk-cleared request ----------

public class BulkClearedRequest
{
    [Required] public DateOnly From { get; set; }
    [Required] public DateOnly To   { get; set; }
    public Guid?  AccountId { get; set; }
    /// <summary>"transaction" | "transfer" | "liabilitypayment" | null (all).</summary>
    public string? Type { get; set; }
}
