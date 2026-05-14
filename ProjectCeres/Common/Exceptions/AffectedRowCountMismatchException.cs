namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown by <see cref="BulkOperationExtensions.ExecuteUpdateExactlyAsync"/> /
/// <see cref="BulkOperationExtensions.ExecuteDeleteExactlyAsync"/> when the actual
/// number of affected rows differs from the caller's expected count. Converts a
/// silent-zero-row failure (e.g. RLS GUC dropped, query predicate now matches no
/// rows) into a loud, traceable exception at the call site.
///
/// <para>
/// <see cref="CallSite"/> is auto-populated from <c>[CallerMemberName]</c> on the
/// helper so log lines name the calling method without the call site repeating
/// itself.
/// </para>
/// </summary>
public sealed class AffectedRowCountMismatchException : InvalidOperationException
{
    public int Expected { get; }
    public int Actual { get; }
    public string Operation { get; }
    public string CallSite { get; }

    public AffectedRowCountMismatchException(int expected, int actual, string operation, string callSite)
        : base(BuildMessage(expected, actual, operation, callSite))
    {
        Expected = expected;
        Actual = actual;
        Operation = operation;
        CallSite = callSite;
    }

    private static string BuildMessage(int expected, int actual, string operation, string callSite) =>
        $"{operation} at {callSite} affected an unexpected number of rows: expected {expected}, actual {actual}.";
}
