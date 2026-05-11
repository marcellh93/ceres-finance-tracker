namespace ProjectCeres.Models;

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AuditLogAction Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string IpAddress { get; set; } = "";
}

public enum AuditLogAction
{
    LoginSucceeded,
    LoginSucceededMfa,
    LoginSucceededBackupCode,
    Logout,
    Registered,
    PasswordResetRequested,
    PasswordResetCompleted,
    EmailChangeRequested,
    EmailChangeConfirmed,
    EmailChangeRevoked,
    MfaEnrolled,
    BackupCodesRegenerated,

    MfaDisabled,              // wired when an MFA-disable endpoint ships
    LockoutSelfServiceUnlock, // wired in Stage 6.10
    DataExportRequested,      // wired in Stage 13
    GdprErasureRequested,     // wired in Stage 13
}
