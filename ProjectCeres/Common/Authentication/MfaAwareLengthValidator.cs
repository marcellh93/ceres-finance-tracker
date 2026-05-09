using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class MfaAwareLengthValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return Task.FromResult(IdentityResult.Success);

        // Stage 6b adds ApplicationUser.MfaEnabled and replaces `false` here.
        var mfaEnrolled = false;
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
