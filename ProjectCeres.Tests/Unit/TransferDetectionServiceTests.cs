using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.ViewModels;

namespace ProjectCeres.Tests.Unit;

public class TransferDetectionServiceTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2024, 3, 15);

    private static ParsedImportRow Row(decimal amount, string desc = "Payment", DateOnly? date = null) => new()
    {
        Date        = date ?? Today,
        Amount      = amount,
        Description = desc
    };

    // ── Intra-file pairing ────────────────────────────────────────────────────

    [Fact]
    public void Detect_IntraFilePair_StagesBothRows()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Transfer out"),
            Row(-100m, "Transfer in"),
        };

        var result = service.Detect(rows, existingCrossAccountTxns: [], exclusionPatterns: [], accountId: AccountId);

        result.StagedRows.Should().HaveCount(2);
        result.StagedRows.Select(s => s.RawAmount).Should().BeEquivalentTo(new[] { 100m, -100m });
    }

    [Fact]
    public void Detect_IntraFilePair_NeitherRowPassedToImport()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Transfer out"),
            Row(-100m, "Transfer in"),
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void Detect_IntraFilePair_SameAmountDifferentDate_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  date: Today),
            Row(-100m, date: Today.AddDays(5)),
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.StagedRows.Should().BeEmpty();
        result.RowIndicesToSkip.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ThreeRowsSameAmount_OnlyFirstPairStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "Out 1"),
            Row(-100m, "In 1"),
            Row(100m,  "Out 2"),
        };

        var result = service.Detect(rows, [], [], AccountId);

        result.StagedRows.Should().HaveCount(2);
        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0, 1 });
    }

    // ── Cross-account pairing ──────────────────────────────────────────────────

    [Fact]
    public void Detect_CrossAccountMatch_StagedWithCandidateId()
    {
        var service = new TransferDetectionService();
        var candidateId = Guid.NewGuid();
        var rows = new List<ParsedImportRow> { Row(-200m, "Wire transfer") };

        var crossAccountTxn = new Transaction
        {
            Id        = candidateId,
            Date      = Today,
            Amount    = 200m,
            AccountId = Guid.NewGuid()
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().HaveCount(1);
        result.StagedRows[0].CandidateTransactionId.Should().Be(candidateId);
        result.RowIndicesToSkip.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void Detect_CrossAccountMatch_WithinOneDayTolerance_Staged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(-50m, "Wire", Today) };
        var crossAccountTxn = new Transaction
        {
            Id     = Guid.NewGuid(),
            Date   = Today.AddDays(1),
            Amount = 50m
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().HaveCount(1);
    }

    [Fact]
    public void Detect_CrossAccountMatch_BeyondOneDayTolerance_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(-50m, "Wire", Today) };
        var crossAccountTxn = new Transaction
        {
            Id     = Guid.NewGuid(),
            Date   = Today.AddDays(2),
            Amount = 50m
        };

        var result = service.Detect(rows, [crossAccountTxn], [], AccountId);

        result.StagedRows.Should().BeEmpty();
    }

    // ── Exclusion / training store ─────────────────────────────────────────────

    [Fact]
    public void Detect_DescriptionMatchesExclusion_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "bizum payment"),
            Row(-100m, "bizum payment"),
        };

        var result = service.Detect(rows, [], exclusionPatterns: ["bizum"], AccountId);

        result.StagedRows.Should().BeEmpty();
        result.RowIndicesToSkip.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ExclusionCaseInsensitive_NotStaged()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow>
        {
            Row(100m,  "BIZUM PAYMENT"),
            Row(-100m, "Bizum Transfer"),
        };

        var result = service.Detect(rows, [], ["bizum"], AccountId);

        result.StagedRows.Should().BeEmpty();
    }

    // ── Staged entity shape ────────────────────────────────────────────────────

    [Fact]
    public void Detect_StagedRow_HasCorrectFields()
    {
        var service = new TransferDetectionService();
        var rows = new List<ParsedImportRow> { Row(77m, "Transfer ABC"), Row(-77m, "Transfer XYZ") };

        var result = service.Detect(rows, [], [], AccountId);

        var staged = result.StagedRows[0];
        staged.AccountId.Should().Be(AccountId);
        staged.Status.Should().Be(StagedTransferStatus.Pending);
        staged.ResolvedAt.Should().BeNull();
    }
}
