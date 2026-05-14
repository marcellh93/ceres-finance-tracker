namespace ProjectCeres.Common.Email;

public sealed class EmailRecipientNotResolvableException : Exception
{
    public Guid UserId { get; }

    public EmailRecipientNotResolvableException(Guid userId, string reason)
        : base($"Could not resolve email recipient for user {userId}: {reason}")
    {
        UserId = userId;
    }
}
