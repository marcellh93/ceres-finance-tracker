using ProjectCeres.Common.Authentication;

namespace ProjectCeres.Tests.Integration.Authentication;

public sealed class HibpStubBreachedPasswordChecker : IBreachedPasswordChecker
{
    private static readonly HashSet<string> Breached = new(StringComparer.Ordinal)
    {
        "password", "Password1!", "qwertyuiop", "letmein123456", "12345678901234567"
    };

    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default)
        => Task.FromResult(Breached.Contains(password));
}
