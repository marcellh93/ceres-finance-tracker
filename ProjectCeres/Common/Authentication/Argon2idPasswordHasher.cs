using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

// Not sealed: the test project subclasses this with CountingArgon2idPasswordHasher to
// count Argon2id invocations per request, replacing the wall-clock-based constant-time
// defence tests (which were flake-prone under integration-test CPU contention) with
// deterministic invocation-count assertions. The class has no internal state that
// subclassing could compromise — every method below creates its own Argon2id instance,
// computes, and returns. Konscious's underlying Argon2id object is not retained.
public class Argon2idPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;
    private const string DummyPlaintext = "dummy-for-timing-fixed";

    private readonly Argon2idOptions _options;

    public Argon2idPasswordHasher(IOptions<Argon2idOptions> options)
    {
        _options = options.Value;
    }

    public virtual string HashPassword(ApplicationUser user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = ComputeHash(password, salt, _options.MemorySizeKb, _options.Iterations, _options.Parallelism);
        return Encode(_options.MemorySizeKb, _options.Iterations, _options.Parallelism, salt, hash);
    }

    public virtual PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (!TryParse(hashedPassword, out var m, out var t, out var p, out var salt, out var expected))
        {
            return PasswordVerificationResult.Failed;
        }

        var actual = ComputeHash(providedPassword, salt, m, t, p);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return (m < _options.MemorySizeKb || t < _options.Iterations || p < _options.Parallelism)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    public virtual void RunDummyHash()
    {
        var salt = new byte[SaltLengthBytes];
        ComputeHash(DummyPlaintext, salt, _options.MemorySizeKb, _options.Iterations, _options.Parallelism);
    }

    private static byte[] ComputeHash(string password, byte[] salt, int memoryKb, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(HashLengthBytes);
    }

    private static string Encode(int m, int t, int p, byte[] salt, byte[] hash) =>
        $"$argon2id$v=19$m={m},t={t},p={p}${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";

    private static bool TryParse(string phc, out int m, out int t, out int p, out byte[] salt, out byte[] hash)
    {
        m = t = p = 0;
        salt = []; hash = [];

        var parts = phc.Split('$', StringSplitOptions.None);
        if (parts.Length != 6) return false;
        if (parts[1] != "argon2id") return false;
        if (parts[2] != "v=19") return false;

        var paramSegment = parts[3];
        var paramPairs = paramSegment.Split(',');
        if (paramPairs.Length != 3) return false;
        foreach (var pair in paramPairs)
        {
            var kv = pair.Split('=');
            if (kv.Length != 2 || !int.TryParse(kv[1], out var v)) return false;
            switch (kv[0])
            {
                case "m": m = v; break;
                case "t": t = v; break;
                case "p": p = v; break;
                default: return false;
            }
        }

        try
        {
            salt = Convert.FromBase64String(PadBase64(parts[4]));
            hash = Convert.FromBase64String(PadBase64(parts[5]));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string PadBase64(string s) =>
        s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
}
