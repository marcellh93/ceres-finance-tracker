using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Authentication;

public class Argon2idPasswordHasherTests
{
    private static Argon2idPasswordHasher CreateHasher() =>
        new(Options.Create(new Argon2idOptions()));

    [Fact]
    public void HashPassword_then_VerifyHashedPassword_returns_Success()
    {
        var hasher = CreateHasher();
        var user = new ApplicationUser();

        var hash = hasher.HashPassword(user, "correct horse battery staple");
        hash.Should().StartWith("$argon2id$v=19$m=19456,t=2,p=1$");

        var result = hasher.VerifyHashedPassword(user, hash, "correct horse battery staple");
        result.Should().Be(PasswordVerificationResult.Success);
    }

    [Fact]
    public void VerifyHashedPassword_returns_Failed_on_wrong_password()
    {
        var hasher = CreateHasher();
        var user = new ApplicationUser();

        var hash = hasher.HashPassword(user, "right-password");
        var result = hasher.VerifyHashedPassword(user, hash, "wrong-password");

        result.Should().Be(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void VerifyHashedPassword_returns_SuccessRehashNeeded_when_stored_params_below_target()
    {
        var weak = new Argon2idPasswordHasher(Options.Create(new Argon2idOptions
        {
            MemorySizeKb = 4096, Iterations = 1, Parallelism = 1
        }));
        var user = new ApplicationUser();
        var weakHash = weak.HashPassword(user, "abc12345");

        var prod = new Argon2idPasswordHasher(Options.Create(new Argon2idOptions()));
        var result = prod.VerifyHashedPassword(user, weakHash, "abc12345");

        result.Should().Be(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void RunDummyHash_completes_within_an_order_of_magnitude_of_real_hash()
    {
        var hasher = new Argon2idPasswordHasher(Options.Create(new Argon2idOptions()));
        var user = new ApplicationUser();

        var realStart = DateTime.UtcNow;
        var hash = hasher.HashPassword(user, "real-password-here");
        hasher.VerifyHashedPassword(user, hash, "real-password-here");
        var realMs = (DateTime.UtcNow - realStart).TotalMilliseconds;

        var dummyStart = DateTime.UtcNow;
        hasher.RunDummyHash();
        var dummyMs = (DateTime.UtcNow - dummyStart).TotalMilliseconds;

        (dummyMs / Math.Max(realMs, 1.0)).Should().BeInRange(0.1, 10.0);
    }
}
