using System.ComponentModel.DataAnnotations;
using ProjectCeres.Models;

namespace ProjectCeres.ViewModels;

public record RecurringTransactionListItemDto(
    Guid     Id,
    string   Name,
    decimal? EstimatedAmount,
    Guid     AccountId,
    string   AccountName,
    string   CurrencySymbol,
    Guid     CategoryId,
    string   CategoryName,
    string   CategoryTypeName,
    string   Frequency,
    int?     DayOfPeriod,
    DateOnly NextDueDate,
    bool     IsActive,
    string   ReminderBehaviour);

public record RecurringTransactionDetailDto(
    Guid     Id,
    string   Name,
    decimal? EstimatedAmount,
    Guid     AccountId,
    Guid     CategoryId,
    string   Frequency,
    int?     DayOfPeriod,
    DateOnly NextDueDate,
    bool     IsActive,
    string   ReminderBehaviour);

public record CreateRecurringTransactionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 999999999999.99)] decimal EstimatedAmount,
    [Required] Guid? AccountId,
    [Required] Guid? CategoryId,
    [Required] string Frequency,
    [Range(1, 31)] int? DayOfPeriod,
    [Required] DateOnly NextDueDate,
    string ReminderBehaviour);

public record UpdateRecurringTransactionRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 999999999999.99)] decimal EstimatedAmount,
    [Required] Guid? AccountId,
    [Required] Guid? CategoryId,
    [Required] string Frequency,
    [Range(1, 31)] int? DayOfPeriod,
    [Required] DateOnly NextDueDate,
    string ReminderBehaviour);

public record ConfirmRecurringTransactionRequest(
    [Required] DateOnly Date,
    [Range(0, 999999999999.99)] decimal Amount,
    string? Description,
    DateOnly? NextDueDate);
