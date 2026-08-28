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

    /// <summary>Delivers an operator's reply to the user, with the reply text and a thread link. Stage 12.6.</summary>
    SupportReplyToUser,

    /// <summary>Notifies the user that the operator marked their ticket Solved. Stage 12.6.</summary>
    SupportTicketSolved,
}
