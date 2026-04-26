// ProjectCeres/Services/ITransferDetectionService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public record TransferDetectionResult(
    IReadOnlyList<ImportStagedTransfer> StagedRows,
    IReadOnlySet<int> RowIndicesToSkip);

public interface ITransferDetectionService
{
    TransferDetectionResult Detect(
        IReadOnlyList<ParsedImportRow> rows,
        IReadOnlyList<Transaction> existingCrossAccountTxns,
        IReadOnlyList<string> exclusionPatterns,
        Guid accountId);
}
