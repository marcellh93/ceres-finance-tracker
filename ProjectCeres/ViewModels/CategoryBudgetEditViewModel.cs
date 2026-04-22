using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CategoryBudgetEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Limit amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Limit amount must be greater than zero.")]
    [Display(Name = "Monthly Limit")]
    public decimal LimitAmount { get; set; }
}
