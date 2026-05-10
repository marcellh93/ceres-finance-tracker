using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Unit.Authentication;

/// <summary>
/// Unit tests for the dynamic password-length validator. Exercises the validator
/// in isolation (no DI scope, no DbContext) — UserManager is passed as null because
/// the validator does not call back into it for the length check.
/// </summary>
public class MfaAwareLengthValidatorTests
{
    private readonly MfaAwareLengthValidator _validator = new();

    [Fact]
    public async Task NonEnrolled_RejectsPasswordBelow15Chars()
    {
        var user = new ApplicationUser { TwoFactorEnabled = false };
        var result = await _validator.ValidateAsync(null!, user, "shortpw");
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "PasswordTooShort");
    }

    [Fact]
    public async Task NonEnrolled_AcceptsPasswordAt15Chars()
    {
        var user = new ApplicationUser { TwoFactorEnabled = false };
        var result = await _validator.ValidateAsync(null!, user, "fifteen-char!!a");
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Enrolled_AcceptsPasswordAt8Chars()
    {
        var user = new ApplicationUser { TwoFactorEnabled = true };
        var result = await _validator.ValidateAsync(null!, user, "8charpwd");
        result.Succeeded.Should().BeTrue("MFA-enrolled users may use shorter passwords per security-model.md");
    }

    [Fact]
    public async Task Enrolled_RejectsPasswordBelow8Chars()
    {
        var user = new ApplicationUser { TwoFactorEnabled = true };
        var result = await _validator.ValidateAsync(null!, user, "7chars!");
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "PasswordTooShort");
    }

    [Fact]
    public async Task NullUser_TreatedAsNonEnrolled()
    {
        // Defensive case: validator is sometimes called with a partially-initialized user
        // (e.g., during Register before SaveChanges). Null/missing TwoFactorEnabled => stricter (15).
        var result = await _validator.ValidateAsync(null!, null!, "8charpwd");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyPassword_AcceptedDeferringToOtherValidators()
    {
        // The length validator short-circuits on null/empty so other validators in the
        // pipeline (e.g. Identity's RequiredLength) can produce the canonical error.
        // Confirms existing behavior is preserved by the fix.
        var user = new ApplicationUser { TwoFactorEnabled = false };
        var result = await _validator.ValidateAsync(null!, user, "");
        result.Succeeded.Should().BeTrue();
    }
}
