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
}
