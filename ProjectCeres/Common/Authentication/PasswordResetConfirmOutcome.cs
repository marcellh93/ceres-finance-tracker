using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Common.Authentication;

public abstract record PasswordResetConfirmOutcome
{
    public sealed record Success : PasswordResetConfirmOutcome;
    public sealed record InvalidToken : PasswordResetConfirmOutcome;
    public sealed record RequiresTotp : PasswordResetConfirmOutcome;
    public sealed record InvalidTotp : PasswordResetConfirmOutcome;
    public sealed record PasswordPolicyViolation(IReadOnlyList<IdentityError> Errors) : PasswordResetConfirmOutcome;
}
