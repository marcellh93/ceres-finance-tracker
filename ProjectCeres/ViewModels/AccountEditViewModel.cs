using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class AccountEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Account name is required.")]
    [StringLength(100, ErrorMessage = "Account name cannot exceed 100 characters.")]
    [Display(Name = "Account Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [Display(Name = "Opening Balance")]
    [Range(typeof(decimal), "-999999999999.99", "999999999999.99",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Opening balance must be between -999,999,999,999.99 and 999,999,999,999.99.")]
    public decimal OpeningBalance { get; set; } = 0;

    [Required(ErrorMessage = "Opening balance date is required.")]
    [Display(Name = "Opening Balance Date")]
    public DateOnly OpeningBalanceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Display(Name = "Repayment Type")]
    public string? LiabilityRepaymentType { get; set; }

    [Display(Name = "Interest Rate")]
    [Range(0.0, 1.0, ErrorMessage = "Interest rate must be between 0 and 1 (e.g. 0.035 for 3.5%).")]
    public decimal? InterestRate { get; set; }
}
