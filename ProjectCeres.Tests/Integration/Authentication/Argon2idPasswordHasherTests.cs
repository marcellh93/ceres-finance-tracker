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
}
