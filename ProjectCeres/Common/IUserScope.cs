namespace ProjectCeres.Common;

/// <summary>
/// Background-job scope holder. HTTP requests resolve user identity from the cookie via
/// <see cref="ICurrentUserAccessor"/>; non-HTTP code paths (hosted services, background jobs)
/// enter via <see cref="EnterAs"/> so the same accessor can return a user id.
/// </summary>
public interface IUserScope
{
    Guid? Current { get; }
    IDisposable EnterAs(Guid userId);
}
