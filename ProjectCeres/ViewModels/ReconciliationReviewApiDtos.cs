namespace ProjectCeres.ViewModels;

public record StagedTransactionDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    MatchedTransactionId,
    string?  MatchedTransactionDescription,
    DateOnly MatchedTransactionDate,
    decimal  MatchedTransactionAmount);
