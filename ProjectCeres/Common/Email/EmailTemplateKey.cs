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

    /// <summary>
    /// Security alert: a sign-in arrived from an IP the user has never signed in from before
    /// (exact-IP novelty; first-ever login suppressed). Stage 12.5.3. Args: {0} new IP,
    /// {1} device summary, {2} sign-in time.
    /// </summary>
    NewSessionAlert,

    /// <summary>
    /// Notifies user that their data export is ready for download. Stage 13.8.
    /// Args: {0} download URL (valid 24 hours, single use).
    /// </summary>
    GdprExportReady,

    /// <summary>
    /// Notifies user that their data export could not be built after the retry
    /// budget was exhausted. Stage 13.8. No args.
    /// </summary>
    GdprExportFailed,
}
