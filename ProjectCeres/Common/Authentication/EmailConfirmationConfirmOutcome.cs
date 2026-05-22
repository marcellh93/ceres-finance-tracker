namespace ProjectCeres.Common.Authentication;

public abstract record EmailConfirmationConfirmOutcome
{
    public sealed record Success(Guid UserId) : EmailConfirmationConfirmOutcome;
    public sealed record InvalidToken : EmailConfirmationConfirmOutcome;
}
