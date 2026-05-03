using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record AccountListItemDto(
    Guid    Id,
    string  Name,
    int     AccountTypeId,
    string  AccountTypeName,
    int     CurrencyId,
    string  CurrencyCode,
    string  CurrencySymbol,
    string? Description,
    bool    IsActive,
    bool    ExcludeFromSpendable,
    string? LiabilityRepaymentType,
    decimal? InterestRate,
    decimal Balance,
    bool    HasTransactions);

public record AccountDetailDto(
    Guid    Id,
    string  Name,
    int     AccountTypeId,
    string  AccountTypeName,
    int     CurrencyId,
    string  CurrencyCode,
    string  CurrencySymbol,
    string? Description,
    bool    IsActive,
    bool    ExcludeFromSpendable,
    string? LiabilityRepaymentType,
    decimal? InterestRate,
    decimal OpeningBalance,
    DateOnly? OpeningBalanceDate);

public record AccountTypeDto(int Id, string Name);

public record CreateAccountRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] int? AccountTypeId,
    [Required] int? CurrencyId,
    [StringLength(500)] string? Description,
    [Range(typeof(decimal), "-999999999999.99", "999999999999.99", ParseLimitsInInvariantCulture = true)] decimal OpeningBalance,
    [Required] DateOnly OpeningBalanceDate,
    string? LiabilityRepaymentType,
    [Range(0.0, 1.0)] decimal? InterestRate,
    bool ExcludeFromSpendable);

public record UpdateAccountRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Description,
    [Range(typeof(decimal), "-999999999999.99", "999999999999.99", ParseLimitsInInvariantCulture = true)] decimal OpeningBalance,
    [Required] DateOnly OpeningBalanceDate,
    string? LiabilityRepaymentType,
    [Range(0.0, 1.0)] decimal? InterestRate,
    bool ExcludeFromSpendable);

public record LedgerEntryDto(
    DateOnly Date,
    DateTime CreatedAt,
    string   Description,
    string   EntryType,
    string?  CategoryName,
    decimal  SignedAmount,
    decimal  RunningBalance);

public record AccountLedgerDto(
    Guid    AccountId,
    string  AccountName,
    string  CurrencySymbol,
    IReadOnlyList<LedgerEntryDto> Entries);
