using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record GoalBudgetListItemDto(
    Guid Id,
    string Name,
    string GoalType,
    string CurrencyCode,
    string CurrencySymbol,
    decimal TargetAmount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description,
    bool IsActive,
    Guid? LinkedAccountId,
    string? LinkedAccountName,
    decimal Progress);

public record GoalBudgetEditDto(
    Guid Id,
    string Name,
    string GoalType,
    int CurrencyId,
    string CurrencyCode,
    decimal TargetAmount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description,
    bool IsActive,
    Guid? LinkedAccountId);

public class CreateGoalBudgetRequest
{
    [Required, MinLength(1)] public string Name { get; set; } = string.Empty;
    [Required] public string GoalType { get; set; } = "Spending";
    public int? CurrencyId { get; set; }
    [Required, Range(0.01, double.MaxValue)] public decimal TargetAmount { get; set; }
    [Required] public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Description { get; set; }
    public Guid? LinkedAccountId { get; set; }
}

public class UpdateGoalBudgetRequest : CreateGoalBudgetRequest
{
    public bool IsActive { get; set; } = true;
}

public record GoalBudgetProgressDto(decimal Progress, decimal Target, int Percentage);
