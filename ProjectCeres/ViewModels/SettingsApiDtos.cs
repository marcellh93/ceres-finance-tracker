using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record UpdateSettingsRequest(
    [Required, RegularExpression("^(comma_decimal|period_decimal)$")] string NumberFormat,
    [Required, RegularExpression("^(DD/MM/YYYY|MM/DD/YYYY|YYYY-MM-DD)$")] string DateFormat,
    [Required] int? DefaultCurrencyId,
    [Range(1, 31)] int PeriodStartDay);
