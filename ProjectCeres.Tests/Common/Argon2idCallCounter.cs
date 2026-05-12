namespace ProjectCeres.Tests.Common;

/// <summary>
/// Test-only singleton that counts Argon2id invocations performed during the lifetime
/// of an HTTP request. Paired with <see cref="CountingArgon2idPasswordHasher"/>, which
/// increments the counter once per `HashPassword`, `VerifyHashedPassword`, or
/// `RunDummyHash` call.
///
/// Constant-time-defence tests use this in place of wall-clock measurement: instead
/// of asserting `|knownDurationMs - unknownDurationMs| &lt; threshold`, they assert
/// `knownCount == unknownCount`. The count is deterministic — by the time the HTTP
/// response returns, every Argon2id call the request made has incremented the counter,
/// because Argon2id within a request handler is synchronous. Zero variance, zero
/// threshold tuning, immune to CPU contention.
///
/// Lifetime: singleton. Reset between branches with <see cref="Reset"/>.
/// Thread-safety: uses <see cref="Interlocked"/> and <see cref="Volatile"/> so
/// concurrent test workloads (race-condition tests) report a stable total.
/// </summary>
public sealed class Argon2idCallCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Volatile.Write(ref _count, 0);
}
