namespace ProjectCeres.Common.Authentication;

public abstract record LockoutUnlockOutcome
{
    public sealed record Success : LockoutUnlockOutcome;
    public sealed record InvalidToken : LockoutUnlockOutcome;
}
