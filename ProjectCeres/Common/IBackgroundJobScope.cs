namespace ProjectCeres.Common;

/// <summary>
/// The single doorway through which background jobs (cross-tenant work outside an
/// HTTP request) enter a user-scoped execution context. Wraps
/// <see cref="IUserScope.EnterAs"/> with a doorway refusal: if no user is declared
/// (<see cref="Guid.Empty"/>), the wrapper throws BEFORE invoking the work
/// delegate. Stage 7.5 / ADR-0067 + ADR-0068.
///
/// <para>
/// Direct calls to <see cref="IUserScope.EnterAs"/> from outside this wrapper are
/// forbidden by an architecture test that scans every <c>EnterAs</c> call site in
/// <c>ProjectCeres/**/*.cs</c>.
/// </para>
/// </summary>
public interface IBackgroundJobScope
{
    Task RunAsync(Guid userId, string jobName, Func<Task> work);
}
