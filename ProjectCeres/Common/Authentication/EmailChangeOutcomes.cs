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
/// Outcome of an authenticated in-app cancel of the caller's own pending change
/// (Stage 12.8.1). NothingPending is not an error to the user — the end state
/// ("no change in flight") is what they wanted — but the endpoint distinguishes it
/// so the UI can refresh rather than claim a cancel that did nothing.
/// </summary>
public abstract record EmailChangeCancelOutcome
{
    public sealed record Cancelled : EmailChangeCancelOutcome;
    public sealed record NothingPending : EmailChangeCancelOutcome;
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
