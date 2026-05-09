using Microsoft.AspNetCore.Identity;
using ProjectCeres.Models;

namespace ProjectCeres.Common.Authentication;

public sealed class BreachedPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private readonly IBreachedPasswordChecker _checker;

    public BreachedPasswordValidator(IBreachedPasswordChecker checker) => _checker = checker;

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return IdentityResult.Success;
        var breached = await _checker.IsBreachedAsync(password);
        return breached
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordBreached",
                Description = "This password has appeared in a public data breach. Choose a different one."
            })
            : IdentityResult.Success;
    }
}
