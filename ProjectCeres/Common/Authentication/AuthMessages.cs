namespace ProjectCeres.Common.Authentication;

/// <summary>
/// User-facing strings shared across auth surfaces. Centralizes copy so multiple
/// envelope-emitters (controller + middleware) cannot drift apart silently before
/// i18n lands.
/// </summary>
public static class AuthMessages
{
    /// <summary>
    /// Envelope message for ACCOUNT_LOCKED_OUT responses. Single source of truth
    /// across AuthController (Login + TOTP paths) and Program.cs OnRejected.
    /// The "15 minutes" string matches the lockout window in
    /// Program.cs:115 (options.Lockout.DefaultLockoutTimeSpan).
    /// </summary>
    public const string AccountTemporarilyLockedFifteenMinutes =
        "Account temporarily locked. Try again in 15 minutes.";
}
