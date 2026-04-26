// ProjectCeres/Services/TransferDetectionService.cs
using ProjectCeres.Models;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Services;

public class TransferDetectionService : ITransferDetectionService
{
    private const int DateToleranceDays = 1;

    public TransferDetectionResult Detect(
        IReadOnlyList<ParsedImportRow> rows,
        IReadOnlyList<Transaction> existingCrossAccountTxns,
        IReadOnlyList<string> exclusionPatterns,
        Guid accountId)
    {
        var staged      = new List<ImportStagedTransfer>();
        var skipIndices = new HashSet<int>();

        var normalizedExclusions = exclusionPatterns
            .Select(p => p.ToLowerInvariant())
            .ToList();

        var pairedIndices = new HashSet<int>();

        for (int i = 0; i < rows.Count; i++)
        {
            if (pairedIndices.Contains(i)) continue;

            var row = rows[i];

            if (IsExcluded(row.Description, normalizedExclusions))
                continue;

            int partnerIdx = FindIntraFilePartner(rows, i, pairedIndices, normalizedExclusions);
            if (partnerIdx >= 0)
            {
                pairedIndices.Add(i);
                pairedIndices.Add(partnerIdx);
                skipIndices.Add(i);
                skipIndices.Add(partnerIdx);

                staged.Add(MakeStagedRow(row, accountId, candidateTransactionId: null));
                staged.Add(MakeStagedRow(rows[partnerIdx], accountId, candidateTransactionId: null));
                continue;
            }

            var crossMatch = FindCrossAccountMatch(row, existingCrossAccountTxns);
            if (crossMatch is not null)
            {
                skipIndices.Add(i);
                staged.Add(MakeStagedRow(row, accountId, candidateTransactionId: crossMatch.Id));
            }
        }

        return new TransferDetectionResult(staged.AsReadOnly(), skipIndices);
    }

    private static int FindIntraFilePartner(
        IReadOnlyList<ParsedImportRow> rows,
        int sourceIdx,
        HashSet<int> alreadyPaired,
        List<string> normalizedExclusions)
    {
        var source = rows[sourceIdx];
        for (int j = sourceIdx + 1; j < rows.Count; j++)
        {
            if (alreadyPaired.Contains(j)) continue;
            var candidate = rows[j];
            if (IsExcluded(candidate.Description, normalizedExclusions)) continue;
            if (candidate.Amount == -source.Amount && candidate.Date == source.Date)
                return j;
        }
        return -1;
    }

    private static Transaction? FindCrossAccountMatch(
        ParsedImportRow row,
        IReadOnlyList<Transaction> crossAccountTxns)
    {
        var absAmount = Math.Abs(row.Amount);
        return crossAccountTxns.FirstOrDefault(t =>
            t.Amount == absAmount &&
            Math.Abs((t.Date.ToDateTime(TimeOnly.MinValue) -
                      row.Date.ToDateTime(TimeOnly.MinValue)).TotalDays) <= DateToleranceDays);
    }

    private static bool IsExcluded(string? description, List<string> normalizedExclusions)
    {
        if (description is null) return false;
        var lower = description.ToLowerInvariant();
        return normalizedExclusions.Any(p => lower.Contains(p));
    }

    private static ImportStagedTransfer MakeStagedRow(
        ParsedImportRow row,
        Guid accountId,
        Guid? candidateTransactionId) => new()
    {
        Id                     = Guid.NewGuid(),
        ImportedAt             = DateTime.UtcNow,
        AccountId              = accountId,
        RawDate                = row.Date,
        RawAmount              = row.Amount,
        RawDescription         = row.Description,
        CandidateTransactionId = candidateTransactionId,
        Status                 = StagedTransferStatus.Pending
    };
}
