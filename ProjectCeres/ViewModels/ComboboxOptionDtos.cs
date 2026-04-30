namespace ProjectCeres.ViewModels;

public record AccountOptionDto(
    Guid Id,
    string Name,
    string CurrencyCode,
    string CurrencySymbol,
    string AccountTypeName);

public record CategoryOptionDto(
    Guid Id,
    string Name,
    string CategoryTypeName);
