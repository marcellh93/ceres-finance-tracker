namespace ProjectCeres.ViewModels.Auth;

/// <summary>
/// Returned by GET /api/auth/me. Used by the SPA's auth context to decide
/// session state and render auth-aware UI. Never serialise ApplicationUser
/// directly — it carries the password hash and security stamp.
/// </summary>
/// <param name="UserId">The user's primary key.</param>
/// <param name="Email">Email address (the canonical user identifier).</param>
/// <param name="TwoFactorEnabled">True if the user has TOTP enrolled.</param>
/// <param name="LastReauthAt">Unix-seconds timestamp of last reauthentication, or null if not yet reauthenticated this session.</param>
/// <param name="BackupCodesRemaining">Count of unused backup codes the user holds (0 if MFA is off).</param>
/// <param name="UsedBackupCodeAtLastLogin">True if the user's most recent successful TOTP step used a backup code rather than the authenticator app — drives the dashboard banner in Phase 4.</param>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    bool TwoFactorEnabled,
    long? LastReauthAt,
    int BackupCodesRemaining,
    bool UsedBackupCodeAtLastLogin);
