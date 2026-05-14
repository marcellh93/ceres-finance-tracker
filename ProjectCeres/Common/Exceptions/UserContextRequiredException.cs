namespace ProjectCeres.Common.Exceptions;

/// <summary>
/// Thrown by <c>ICurrentUserAccessor.Require()</c> when the caller needs a
/// <see cref="UserContext.Resolved"/> context and gets anything else. Stage 7.6.7 / ADR-0073.
///
/// <para>
/// Replaces the prior pattern where service code that needed a real user would silently
/// read <see cref="ICurrentUserAccessor.UserId"/> as <see cref="Guid.Empty"/> and then
/// have its EF query return zero rows — the diagnostic surface for "you forgot to bind a
/// user" looked identical to "RLS correctly filtered the rows."
/// </para>
///
/// <para>
/// The exception message names which case fired (<c>PreAuth</c>, <c>Background</c>,
/// <c>Uninitialized</c>) so the failure attributes itself.
/// </para>
/// </summary>
public sealed class UserContextRequiredException : InvalidOperationException
{
    public string Expected { get; }
    public string Actual { get; }

    public UserContextRequiredException(string expected, string actual)
        : base(BuildMessage(expected, actual))
    {
        Expected = expected;
        Actual = actual;
    }

    private static string BuildMessage(string expected, string actual) =>
        $"Operation requires a {expected} UserContext but the current context is {actual}.";
}
