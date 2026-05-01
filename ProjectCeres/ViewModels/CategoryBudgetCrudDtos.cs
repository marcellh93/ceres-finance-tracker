using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record CategoryBudgetListItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CurrencyCode,
    string CurrencySymbol,
    decimal LimitAmount,
    bool IsActive,
    decimal CurrentPeriodSpend,
    DateOnly CurrentPeriodEnd);

public record CategoryBudgetEditDto(
    Guid Id,
    Guid CategoryId,
    int CurrencyId,
    string CurrencyCode,
    decimal LimitAmount,
    bool IsActive);

public class CreateCategoryBudgetRequest
{
    [Required] public Guid? CategoryId { get; set; }
    [Required] public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal LimitAmount { get; set; }
}

public class UpdateCategoryBudgetRequest
{
    [Required] public Guid? CategoryId { get; set; }
    [Required] public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal LimitAmount { get; set; }
    public bool IsActive { get; set; } = true;
}

public record CategoryBudgetSpendDto(
    decimal Spent,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);
