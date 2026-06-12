namespace ProjectCeres.Common.Authentication;

/// <summary>
/// E2E-only checker that treats every password as not-breached, so E2E registration
/// never makes a live HIBP API call (an outage would otherwise 500 the register endpoint
/// and flake the suite). Registered ONLY under ASPNETCORE_ENVIRONMENT=E2E; the real
/// HaveIBeenPwnedPasswordChecker keeps its production coverage.
/// </summary>
public sealed class AlwaysAllowBreachedPasswordChecker : IBreachedPasswordChecker
{
    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default) =>
        Task.FromResult(false);
}
