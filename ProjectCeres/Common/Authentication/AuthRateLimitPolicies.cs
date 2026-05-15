namespace ProjectCeres.Common.Authentication;

public static class AuthRateLimitPolicies
{
    /// <summary>10/min/IP sliding window. Applied to /login, /register.</summary>
    public const string AuthLoginByIp = "auth-login-by-ip";

    /// <summary>60/min/IP sliding window. Applied to /csrf only — separate bucket so token
    /// refresh churn (SPA tab-flap, multi-tab) doesn't lock the IP out of login.
    /// Stage 6b.3 Gap 7.</summary>
    public const string AuthCsrfByIp = "auth-csrf-by-ip";

    /// <summary>10/min/user sliding window keyed off Identity.TwoFactorUserId. Applied to /login/totp.</summary>
    public const string AuthTotpByUser = "auth-totp-by-user";

    /// <summary>Fallback partition key for /login/totp requests with no MFA-pending cookie.
    /// Routes them into a single shared bucket so an attacker can't dodge the limit by stripping the cookie.</summary>
    public const string AnonymousTotpPartition = "anonymous-totp";

    /// <summary>10/min/user sliding window keyed off authenticated NameIdentifier claim.
    /// Applied to all MfaController endpoints (Enroll, EnrollVerify, RegenerateBackupCodes).
    /// Stage 6b.3 follow-up to Gap 4.</summary>
    public const string AuthMfaByUser = "auth-mfa-by-user";

    /// <summary>5/hour per email sliding window. Applied via service-side MemoryCache gate
    /// inside PasswordResetService.RequestAsync. Keyed by lowercased trimmed email so a
    /// single account can't be spammed with reset emails. Stage 6c.1.</summary>
    public const string AuthPasswordResetByEmail = "auth-password-reset-by-email";

    /// <summary>Fallback partition key for /password-reset/request with malformed email.
    /// Routes them into a single shared bucket so an attacker can't dodge the limit by
    /// sending junk. Stage 6c.1.</summary>
    public const string AnonymousPasswordResetPartition = "anonymous-password-reset";

    /// <summary>10/min/user sliding window keyed off authenticated NameIdentifier claim.
    /// Applied to POST /api/auth/reauth. Stage 6c.2.</summary>
    public const string AuthReauthByUser = "auth-reauth-by-user";

    /// <summary>5/hour sliding-window middleware policy for email-triggering endpoints.
    /// Partition key order: normalized email from the JSON body → NameIdentifier of an
    /// authenticated caller (fallback for /email-change/request whose body has no
    /// "email" field) → client IP → "anonymous-email" constant. Stage 8d. Partitioning
    /// by the body email (not UserId) on unauthenticated paths preserves the Stage 6.16
    /// fix: unknown and known emails go through the same limiter path so the
    /// Argon2id-call-count timing channel stays closed.</summary>
    public const string EmailByUser = "email-by-user";

    /// <summary>10/hour/IP sliding-window middleware policy applied alongside
    /// <see cref="EmailByUser"/> on email-triggering endpoints. Backstops the per-email
    /// policy against an attacker who rotates the "email" payload across many addresses
    /// from a single source. Stage 8d.</summary>
    public const string EmailByIp = "email-by-ip";
}
