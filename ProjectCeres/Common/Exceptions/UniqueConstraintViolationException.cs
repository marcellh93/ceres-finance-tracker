using Npgsql;

namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown when a database write hits Postgres SqlState <c>23505</c> (unique violation).
/// Wraps the original <see cref="PostgresException"/> and carries the violated
/// constraint name so service code can branch on a specific unique index without
/// re-inspecting the inner exception.
///
/// <para>
/// Service code that needs to surface "this email is already in use" or "this account
/// name is already taken" to the user can <c>catch (UniqueConstraintViolationException)</c>
/// instead of <c>catch (DbUpdateException ex) when (ex.InnerException is PostgresException pe and pe.SqlState == "23505")</c>.
/// </para>
///
/// <para>
/// <see cref="ConstraintName"/> may be <c>null</c> in the rare case where Postgres
/// did not populate the structured field — Postgres's protocol docs say "frontends
/// should not assume the presence of any of these fields." Empirical PG 18.3 testing
/// against <c>project_ceres_test</c> showed it populates reliably for 23505 emitted
/// from primary-key and unique-index violations, but the typed exception still wraps
/// the failure when the field is absent so callers don't lose the typed catch.
/// </para>
/// </summary>
public sealed class UniqueConstraintViolationException : InvalidOperationException
{
    public string? TableName { get; }
    public string? ConstraintName { get; }
    public PostgresException OriginalException { get; }

    public UniqueConstraintViolationException(string? tableName, string? constraintName, PostgresException originalException)
        : base(BuildMessage(tableName, constraintName), originalException)
    {
        TableName = tableName;
        ConstraintName = constraintName;
        OriginalException = originalException;
    }

    private static string BuildMessage(string? tableName, string? constraintName) =>
        $"Unique constraint violation on table \"{tableName ?? "(unknown)"}\", " +
        $"constraint \"{constraintName ?? "(unknown)"}\". Postgres SqlState 23505.";
}
