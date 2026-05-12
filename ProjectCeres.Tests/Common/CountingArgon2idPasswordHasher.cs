using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Test-only subclass of <see cref="Argon2idPasswordHasher"/> that increments an
/// <see cref="Argon2idCallCounter"/> on every invocation, then delegates to the base.
/// Used by constant-time-defence tests to assert that two HTTP branches perform the
/// same number of Argon2id operations — a deterministic replacement for the
/// previously-flaky wall-clock-based timing tests.
///
/// Registered via <c>AuthTestWebApplicationFactory.WithArgon2idCounter(out var counter)</c>.
/// </summary>
public sealed class CountingArgon2idPasswordHasher : Argon2idPasswordHasher
{
    private readonly Argon2idCallCounter _counter;

    public CountingArgon2idPasswordHasher(IOptions<Argon2idOptions> options, Argon2idCallCounter counter)
        : base(options)
    {
        _counter = counter;
    }

    public override string HashPassword(ApplicationUser user, string password)
    {
        _counter.Increment();
        return base.HashPassword(user, password);
    }

    public override PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        _counter.Increment();
        return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }

    public override void RunDummyHash()
    {
        _counter.Increment();
        base.RunDummyHash();
    }
}
