namespace ProjectCeres.ViewModels;

public record MovementListItemDto(
    Guid Id,
    string MovementType,
    DateOnly Date,
    decimal Amount,
    string CurrencyCode,
    string CurrencySymbol,
    string? Description,
    bool IsCleared,
    string? AccountName,
    string? CategoryName,
    string? CategoryTypeName,
    string? SourceAccountName,
    string? DestAccountName,
    string? AssetAccountName,
    string? LiabilityAccountName);

public record MovementsPageDto(
    IReadOnlyList<MovementListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
