namespace ProjectCeres.Common.Email;

public enum EmailTemplateKey
{
    PasswordResetRequest,
    PasswordChanged,
    PasswordResetCancelledEmailChange,
    EmailChangeVerifyNew,
    EmailChangeRevokeOld,
    EmailChangeConfirmed,
    EmailChangeConfirmedToOld,
    EmailChangeRevokeNotificationToOld,
    LockoutUnlock,
    RegistrationConfirmation,
    TotpEnrolled,
    TotpDisabled,
    BackupCodesRegenerated,

    /// <summary>Notifies the support address that a user filed a ticket. Stage 12.5.</summary>
    SupportTicketReceived,
}
