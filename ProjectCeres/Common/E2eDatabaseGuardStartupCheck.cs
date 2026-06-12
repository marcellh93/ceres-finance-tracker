using Npgsql;

namespace ProjectCeres.Common;

/// <summary>
/// Refuses to start under ASPNETCORE_ENVIRONMENT=E2E unless the application connection
/// points at the dedicated <c>project_ceres_e2e</c> database. Collapses the env-flip
/// blast radius: a stray E2E env on a real host would otherwise file-sink auth tokens,
/// raise rate limits, and disable breached-password screening all at once.
/// </summary>
public static class E2eDatabaseGuardStartupCheck
{
    public const string ExpectedDatabase = "project_ceres_e2e";

    public static async Task EnsureConnectedToE2eDatabaseAsync(
        string applicationConnectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(applicationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT current_database()";
        var actual = (string?)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(actual, ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"E2E environment refuses to start: ApplicationConnection points at '{actual}', "
                + $"expected '{ExpectedDatabase}'. Refusing to risk a non-E2E database.");
    }
}
