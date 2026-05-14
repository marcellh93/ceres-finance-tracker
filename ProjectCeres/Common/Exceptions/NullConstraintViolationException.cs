using Npgsql;

namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown when a database write hits Postgres SqlState <c>23502</c> (NOT NULL
/// violation). Wraps the original <see cref="PostgresException"/> and carries the
/// offending column name.
///
/// <para>
/// In application code, a 23502 typically signals a model/migration drift bug —
/// either the entity model omitted a <c>required</c> property the schema enforces,
/// or a service forgot to populate a non-nullable column. The typed exception lets
/// service code log a structured "column X is required" message without parsing
/// <see cref="PostgresException.MessageText"/>.
/// </para>
///
/// <para>
/// <see cref="ColumnName"/> may be <c>null</c> in the rare case Postgres did not
/// populate the structured field (see <see cref="UniqueConstraintViolationException"/>
/// for the protocol-docs note).
/// </para>
/// </summary>
public sealed class NullConstraintViolationException : InvalidOperationException
{
    public string? TableName { get; }
    public string? ColumnName { get; }
    public PostgresException OriginalException { get; }

    public NullConstraintViolationException(string? tableName, string? columnName, PostgresException originalException)
        : base(BuildMessage(tableName, columnName), originalException)
    {
        TableName = tableName;
        ColumnName = columnName;
        OriginalException = originalException;
    }

    private static string BuildMessage(string? tableName, string? columnName) =>
        $"NOT NULL constraint violation on table \"{tableName ?? "(unknown)"}\", " +
        $"column \"{columnName ?? "(unknown)"}\". Postgres SqlState 23502.";
}
