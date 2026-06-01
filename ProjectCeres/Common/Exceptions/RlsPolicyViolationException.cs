using Npgsql;

namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown when a database write hits Postgres SqlState <c>42501</c> on one of the
/// user-owned tables in <see cref="UserOwnedModel.RlsTables"/>. Wraps the original
/// <see cref="PostgresException"/> and carries the diagnostic data needed to
/// distinguish an RLS policy rejection from any other 42501 (missing GRANT,
/// read-only table, etc.).
///
/// <para>
/// Two reasons it might fire on the runtime <c>ceres_app</c> role:
/// </para>
/// <list type="number">
///   <item>The application attempted an INSERT or UPDATE that would write a
///         <c>UserId</c> different from the GUC <c>app.current_user_ref</c>.
///         The RLS <c>user_isolation</c> policy's WITH CHECK clause rejected it.</item>
///   <item>The GUC was not set (<see cref="GucUserId"/> is <c>null</c>) — a
///         BackgroundJobScope leak or a test that forgot to bind a user.</item>
/// </list>
///
/// <para>
/// Service code can <c>catch (RlsPolicyViolationException)</c> to distinguish
/// these from foreign-key, unique-constraint, or NOT NULL violations.
/// </para>
/// </summary>
public sealed class RlsPolicyViolationException : InvalidOperationException
{
    public string TableName { get; }
    public Guid? AttemptedUserId { get; }
    public Guid? GucUserId { get; }
    public PostgresException OriginalException { get; }

    public RlsPolicyViolationException(
        string tableName,
        Guid? attemptedUserId,
        Guid? gucUserId,
        PostgresException originalException)
        : base(BuildMessage(tableName, attemptedUserId, gucUserId), originalException)
    {
        TableName = tableName;
        AttemptedUserId = attemptedUserId;
        GucUserId = gucUserId;
        OriginalException = originalException;
    }

    private static string BuildMessage(string tableName, Guid? attemptedUserId, Guid? gucUserId)
    {
        var attempted = attemptedUserId is { } a ? a.ToString("D") : "(unknown)";
        var guc = gucUserId is { } g ? g.ToString("D") : "(unset)";
        return $"Row-Level Security policy rejected a write on table \"{tableName}\". " +
               $"Attempted UserId: {attempted}. Current GUC app.current_user_ref: {guc}. " +
               $"Postgres SqlState 42501.";
    }
}
