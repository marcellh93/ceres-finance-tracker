using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class BudgetCreateViewModel
{
    [Required(ErrorMessage = "Budget name is required.")]
    [StringLength(100, ErrorMessage = "Budget name cannot exceed 100 characters.")]
    [Display(Name = "Budget Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Target amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Target amount must be greater than zero.")]
    [Display(Name = "Target Amount")]
    public decimal TargetAmount { get; set; }

    [Required(ErrorMessage = "Please select a currency.")]
    [Display(Name = "Currency")]
    public int? CurrencyId { get; set; }

    [Required(ErrorMessage = "Start date is required.")]
    [Display(Name = "Start Date")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Display(Name = "End Date")]
    public DateOnly? EndDate { get; set; }

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}
