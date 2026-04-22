using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class CategoryBudgetCreateViewModel
{
    [Required(ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    [Required(ErrorMessage = "Please select a currency.")]
    [Display(Name = "Currency")]
    public int? CurrencyId { get; set; }

    [Required(ErrorMessage = "Limit amount is required.")]
    [Range(0.01, 999999999999.99, ErrorMessage = "Limit amount must be greater than zero.")]
    [Display(Name = "Monthly Limit")]
    public decimal LimitAmount { get; set; }
}
