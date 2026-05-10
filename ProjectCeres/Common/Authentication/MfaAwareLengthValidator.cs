using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class MfaAwareLengthValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return Task.FromResult(IdentityResult.Success);

        // Per security-model.md § Passwords: MFA-enrolled users may use 8-char minimum
        // (defense-in-depth from the second factor). Non-enrolled users keep the 15-char
        // floor. Stage 6b.3 Gap 10 (wired what 6b1's design always intended).
        var mfaEnrolled = user?.TwoFactorEnabled == true;
        var minLength = mfaEnrolled ? 8 : 15;

        if (password.Length < minLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooShort",
                Description = $"Password must be at least {minLength} characters."
            }));
        }
        return Task.FromResult(IdentityResult.Success);
    }
}
