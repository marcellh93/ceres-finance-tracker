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
        IReadOnlyList<LiabilityPayment> existingLiabilityPayments,
        IReadOnlyList<Transfer> existingTransfers,
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
                continue;
            }

            // LiabilityPayment / Transfer matches: the existing record represents both
            // sides of the movement, so there's no Transaction row to link against.
            // Stage with no candidate id so the user can confirm/dismiss in the review tab.
            if (HasMatchingLiabilityPayment(row, existingLiabilityPayments, accountId)
                || HasMatchingTransfer(row, existingTransfers, accountId))
            {
                skipIndices.Add(i);
                staged.Add(MakeStagedRow(row, accountId, candidateTransactionId: null));
            }
        }

        return new TransferDetectionResult(staged.AsReadOnly(), skipIndices);
    }

    private static bool HasMatchingLiabilityPayment(
        ParsedImportRow row,
        IReadOnlyList<LiabilityPayment> payments,
        Guid accountId)
    {
        var absAmount = Math.Abs(row.Amount);
        return payments.Any(p =>
            (p.AssetAccountId == accountId || p.LiabilityAccountId == accountId) &&
            p.Amount == absAmount &&
            WithinDateTolerance(p.Date, row.Date));
    }

    private static bool HasMatchingTransfer(
        ParsedImportRow row,
        IReadOnlyList<Transfer> transfers,
        Guid accountId)
    {
        var absAmount = Math.Abs(row.Amount);
        return transfers.Any(t =>
            (t.SourceAccountId == accountId || t.DestAccountId == accountId) &&
            t.Amount == absAmount &&
            WithinDateTolerance(t.Date, row.Date));
    }

    private static bool WithinDateTolerance(DateOnly a, DateOnly b) =>
        Math.Abs((a.ToDateTime(TimeOnly.MinValue) - b.ToDateTime(TimeOnly.MinValue)).TotalDays) <= DateToleranceDays;

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
