namespace ProjectCeres.Common.Authentication;

public abstract record EmailChangeRequestOutcome
{
    public sealed record Accepted : EmailChangeRequestOutcome;
    public sealed record EmailAlreadyInUse : EmailChangeRequestOutcome;
    public sealed record EmailUnchanged : EmailChangeRequestOutcome;
}

public abstract record EmailChangeConfirmOutcome
{
    public sealed record Success : EmailChangeConfirmOutcome;
    public sealed record InvalidToken : EmailChangeConfirmOutcome;
    public sealed record EmailAlreadyInUse : EmailChangeConfirmOutcome;
}

public abstract record EmailChangeRevokeOutcome
{
    public sealed record Success : EmailChangeRevokeOutcome;
    public sealed record InvalidToken : EmailChangeRevokeOutcome;
}

/// <summary>
/// An email change awaiting confirmation. <paramref name="MaskedNewEmail"/> is already
/// masked — the full address never leaves the server, so devtools cannot defeat the
/// masking the settings banner relies on.
/// </summary>
public sealed record EmailChangePending(
    string MaskedNewEmail,
    DateTime ExpiresAt,
    bool Expired);
