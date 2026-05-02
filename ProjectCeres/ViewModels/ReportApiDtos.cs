namespace ProjectCeres.ViewModels;

public record TransactionHistoryRowDto(
    Guid     Id,
    DateOnly Date,
    string   AccountName,
    string   CategoryName,
    string   CategoryTypeName,
    string?  Description,
    decimal  Amount,
    string   CurrencySymbol);
