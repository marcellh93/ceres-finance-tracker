namespace ProjectCeres.Services;

public record TransactionExportRow(
    DateOnly Date,
    string   Account,
    string   Category,
    string   CategoryType,
    string?  Description,
    decimal  Amount);
