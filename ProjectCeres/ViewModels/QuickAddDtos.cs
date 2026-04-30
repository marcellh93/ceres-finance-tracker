using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CreateTransactionRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    public Guid? CategoryId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

public class CreateTransferRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select a source account.")]
    public Guid? SourceAccountId { get; set; }

    [Required(ErrorMessage = "Please select a destination account.")]
    public Guid? DestAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}

public class CreateLiabilityPaymentRequest
{
    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "Please select an asset account.")]
    public Guid? AssetAccountId { get; set; }

    [Required(ErrorMessage = "Please select a liability account.")]
    public Guid? LiabilityAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}
