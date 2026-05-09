namespace ProjectCeres.Common.Authentication;

public interface IBreachedPasswordChecker
{
    Task<bool> IsBreachedAsync(string password, CancellationToken ct = default);
}
