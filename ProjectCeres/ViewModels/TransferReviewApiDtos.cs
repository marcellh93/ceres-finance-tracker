using System.ComponentModel.DataAnnotations;

namespace ProjectCeres.ViewModels;

public record StagedTransferDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    string   AccountCurrencyCode,
    string   AccountCurrencySymbol,
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    CandidateTransactionId,
    string?  CandidateTransactionDescription,
    DateOnly? CandidateTransactionDate,
    decimal? CandidateTransactionAmount);

public record TransferReviewActionRequest(
    [Required] Guid? OtherAccountId);
