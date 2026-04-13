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
}
