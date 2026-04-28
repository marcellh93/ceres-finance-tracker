namespace ProjectCeres.Services;

public interface ITransactionExportService
{
    Task<IReadOnlyList<TransactionExportRow>> ExportAsync(
        Guid?     accountId = null,
        DateOnly? from      = null,
        DateOnly? to        = null);
}
