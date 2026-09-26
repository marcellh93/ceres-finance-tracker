namespace ProjectCeres.Services;

public abstract record ErasureCancelOutcome
{
    public sealed record Cancelled : ErasureCancelOutcome;
    public sealed record NotFound : ErasureCancelOutcome;
    /// <summary>Token matched a request that is already Completed (or its window elapsed) — un-cancellable.</summary>
    public sealed record Gone : ErasureCancelOutcome;
}
