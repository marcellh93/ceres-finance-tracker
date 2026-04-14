using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class TransactionEditViewModel
{
    public Guid Id { get; set; }

    /// <summary>"Regular" or "LiabilityPayment"</summary>
    public string TransactionType { get; set; } = "Regular";

    [Required(ErrorMessage = "Date is required.")]
    public DateOnly Date { get; set; }

    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    [Display(Name = "Account")]
    public Guid? AccountId { get; set; }

    // Regular transaction fields
    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    [Display(Name = "Budget")]
    public Guid? BudgetId { get; set; }

    // LiabilityPayment field
    [Display(Name = "Liability Account")]
    public Guid? LiabilityAccountId { get; set; }
}
