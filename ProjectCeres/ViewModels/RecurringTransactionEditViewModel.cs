using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public class RecurringTransactionEditViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Reminder name is required.")]
    [StringLength(100, ErrorMessage = "Reminder name cannot exceed 100 characters.")]
    [Display(Name = "Reminder Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Estimated Amount")]
    [Range(0, 999999999999.99, ErrorMessage = "Estimated amount must be zero or greater.")]
    public decimal EstimatedAmount { get; set; }

    [Required(ErrorMessage = "Please select an account.")]
    [Display(Name = "Account")]
    public Guid? AccountId { get; set; }

    [Required(ErrorMessage = "Please select a category.")]
    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    [Required(ErrorMessage = "Please select a frequency.")]
    public Frequency Frequency { get; set; }

    [Display(Name = "Day of Period")]
    [Range(1, 31, ErrorMessage = "Day of period must be between 1 and 31.")]
    public int? DayOfPeriod { get; set; }

    [Required(ErrorMessage = "Next due date is required.")]
    [Display(Name = "Next Due Date")]
    public DateOnly NextDueDate { get; set; }
}
