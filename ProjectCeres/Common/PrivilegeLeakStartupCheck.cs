using Npgsql;

namespace ProjectCeres.Common;

/// <summary>
/// Refuses to start the application if the configured <c>ApplicationConnection</c>
/// has DDL privileges. The runtime role (<c>ceres_app</c>, Stage 7.5 / ADR-0068) must
/// NOT have <c>CREATE</c> rights on the schema — a misconfiguration that wires it to
/// <c>ceres_migrator</c> or <c>postgres</c> would let the app issue DDL at runtime and
/// also (because the privileged roles have <c>BYPASSRLS</c>) silently bypass Row-Level
/// Security policies. Either failure is a security incident.
///
/// Mechanism: open a transaction, attempt <c>CREATE TABLE _privilege_check_&lt;guid&gt; (id int)</c>,
/// roll back. If the CREATE succeeds, throw and exit. If Postgres rejects with
/// <c>42501 insufficient_privilege</c>, the check passes.
/// </summary>
public static class PrivilegeLeakStartupCheck
{
    public static async Task EnsureApplicationConnectionLacksDdlAsync(
        string applicationConnectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(applicationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var probeTable = $"_privilege_check_{Guid.NewGuid():N}";

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"CREATE TABLE \"{probeTable}\" (id int)";

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            // Expected — ceres_app has no DDL rights. Roll back the failed CREATE and return.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // CREATE TABLE succeeded: the application connection string is wired to a
        // privileged role. Roll back the probe and refuse to start.
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            "Application connection string is wired to a privileged role — refusing to start. " +
            "Expected ceres_app (no DDL, no BYPASSRLS). Check ConnectionStrings:ApplicationConnection " +
            "and re-run scripts/setup-postgres-roles.sql if needed.");
    }
}
