using Npgsql;

namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown when a database write hits Postgres SqlState <c>23503</c> (foreign-key
/// violation). Wraps the original <see cref="PostgresException"/> and carries the
/// violated FK constraint name.
///
/// <para>
/// Surfaces both directions of the violation: an INSERT/UPDATE that references a
/// non-existent parent key, AND a DELETE that would orphan child rows. Service code
/// can <c>catch (ForeignKeyViolationException)</c> to surface "this account is still
/// referenced by transactions" or "this category does not exist" without re-inspecting
/// the inner exception.
/// </para>
///
/// <para>
/// <see cref="ConstraintName"/> may be <c>null</c> in the rare case Postgres did not
/// populate the structured field (see <see cref="UniqueConstraintViolationException"/>
/// for the protocol-docs note).
/// </para>
/// </summary>
public sealed class ForeignKeyViolationException : InvalidOperationException
{
    public string? TableName { get; }
    public string? ConstraintName { get; }
    public PostgresException OriginalException { get; }

    public ForeignKeyViolationException(string? tableName, string? constraintName, PostgresException originalException)
        : base(BuildMessage(tableName, constraintName), originalException)
    {
        TableName = tableName;
        ConstraintName = constraintName;
        OriginalException = originalException;
    }

    private static string BuildMessage(string? tableName, string? constraintName) =>
        $"Foreign-key constraint violation on table \"{tableName ?? "(unknown)"}\", " +
        $"constraint \"{constraintName ?? "(unknown)"}\". Postgres SqlState 23503.";
}
