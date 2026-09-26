using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>Disjoint partition of <see cref="UserContentEntities"/> by erasure treatment.</summary>
public sealed record ErasureLanePartition(
    IReadOnlyList<string> Purge,
    IReadOnlyList<string> Statutory,
    IReadOnlyList<string> Support);

/// <summary>
/// Splits the user-content tables (Stage 13.8) into the three erasure lanes: statutory
/// (anonymise-and-retain), support (redact-retain), purge (hard-delete). Every content
/// table falls into exactly one lane — see ErasureLaneCoverageTests.
/// </summary>
public static class ErasureLanes
{
    public static ErasureLanePartition Classify(IReadOnlyModel model)
    {
        var content = UserContentEntities.List(model).Select(t => t.PostgresTableName).ToList();

        var statutory = content.Where(t => StatutoryRetentionSet.Tables.Contains(t)).ToList();
        var support = content.Where(t => UserContentEntities.SupportContentTables.Contains(t)).ToList();
        var purge = content.Except(statutory).Except(support).ToList();

        return new ErasureLanePartition(purge, statutory, support);
    }
}
