namespace ProjectCeres.Services;

public record TransactionExportRow(
    DateOnly Date,
    string   Account,
    string   Category,
    string   CategoryType,
    string?  Description,
    decimal  Amount);

public interface ITransactionExportService
{
    Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
        Guid?     accountId = null,
        DateOnly? from      = null,
        DateOnly? to        = null);
}
