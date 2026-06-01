using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace ProjectCeres.Common;

/// <summary>
/// Refuses to start if any applied user-owned table (<see cref="UserOwnedModel.RlsTables"/>
/// whose table physically exists) lacks both <c>relrowsecurity</c> and <c>relforcerowsecurity</c>.
/// Tables whose creating migration is still pending (probed via <c>to_regclass</c>) are skipped,
/// so a rolling deploy with the binary up but the migration not yet applied does not crash.
/// The full model-vs-live-DB comparison runs in the test suite (Stage 9.5b / D3).
/// </summary>
public static class RlsParityStartupCheck
{
    public static async Task EnsureAppliedUserOwnedTablesAreRlsProtectedAsync(
        IReadOnlyModel model, string applicationConnectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(applicationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var unprotected = new List<string>();
        foreach (var table in UserOwnedModel.RlsTables(model))
        {
            if (!await TableExistsAsync(connection, table.PostgresTableName, cancellationToken).ConfigureAwait(false))
                continue; // creating migration not yet applied — skip, no deploy-race crash

            var (rowSecurity, forceRowSecurity) =
                await ReadRlsFlagsAsync(connection, table.PostgresTableName, cancellationToken).ConfigureAwait(false);
            if (!rowSecurity || !forceRowSecurity)
                unprotected.Add(table.PostgresTableName);
        }

        if (unprotected.Count > 0)
            throw new InvalidOperationException(
                "Applied user-owned tables lack forced RLS — refusing to start: "
                + string.Join(", ", unprotected.OrderBy(name => name, StringComparer.Ordinal))
                + ". Add ENABLE + FORCE ROW LEVEL SECURITY and a user_isolation policy in a migration. Stage 9.5b.");
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT to_regclass(@qualified) IS NOT NULL";
        cmd.Parameters.AddWithValue("qualified", $"public.\"{tableName}\"");
        return (bool)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
    }

    private static async Task<(bool RowSecurity, bool ForceRowSecurity)> ReadRlsFlagsAsync(
        NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT c.relrowsecurity, c.relforcerowsecurity
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = @table";
        cmd.Parameters.AddWithValue("table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (false, false); // table vanished between the probe and here — treat as unprotected
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }
}
