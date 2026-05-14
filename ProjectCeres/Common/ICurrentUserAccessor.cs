namespace ProjectCeres.Common;

/// <summary>
/// Resolves the user the current request / job is acting as.
///
/// <para>
/// Stage 7.6.7 / ADR-0073: <see cref="Context"/> is the typed contract. <see cref="UserId"/>
/// is preserved as a convenience accessor returning <see cref="UserContext.Resolved.UserId"/>
/// when the context is <see cref="UserContext.Resolved"/>, and <see cref="Guid.Empty"/>
/// otherwise. The convenience accessor keeps the EF global query filter expression
/// <c>e.UserId == _currentUser.UserId</c> working without rewriting every filter
/// registration; new service code that needs a real user should call
/// <c>_currentUser.Require()</c> from <see cref="CurrentUserAccessorExtensions"/> instead.
/// </para>
/// </summary>
public interface ICurrentUserAccessor
{
    UserContext Context { get; }

    Guid UserId => Context is UserContext.Resolved r ? r.UserId : Guid.Empty;
}
