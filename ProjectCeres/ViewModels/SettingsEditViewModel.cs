using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public class SettingsEditViewModel
{
    [Required(ErrorMessage = "Please select a number format.")]
    [Display(Name = "Number Format")]
    public string NumberFormat { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a date format.")]
    [Display(Name = "Date Format")]
    public string DateFormat { get; set; } = string.Empty;

[Required(ErrorMessage = "Please select a default currency.")]
    [Display(Name = "Default Currency")]
    public int? DefaultCurrencyId { get; set; }

    [Range(1, 31, ErrorMessage = "Monthly cycle start day must be between 1 and 31.")]
    [Display(Name = "Monthly cycle start day")]
    public int PeriodStartDay { get; set; } = 1;
}
