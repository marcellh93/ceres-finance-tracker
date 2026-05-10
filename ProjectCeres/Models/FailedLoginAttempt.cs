namespace ProjectCeres.Models;

public sealed class FailedLoginAttempt
{
    public Guid Id { get; set; }
    public string? EmailAttempted { get; set; }
    public Guid? UserId { get; set; }
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public FailedLoginReason Reason { get; set; }
    public DateTime OccurredAt { get; set; }
}

public enum FailedLoginReason
{
    BadCredentials,
    BadTotp,
    BadBackupCode,
    LockedOut,
    UnknownUser,
    PasswordResetUnknownEmail,
}
